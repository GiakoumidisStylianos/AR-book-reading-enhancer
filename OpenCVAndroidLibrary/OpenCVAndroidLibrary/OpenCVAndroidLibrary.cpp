#include <opencv2/highgui.hpp>
#include <opencv2/imgproc.hpp>
#include <opencv2/calib3d.hpp>

#define EXTERNAL_FUNC extern "C" void

using namespace cv;

// BRISK variables.
const int THRESHOLD = 40; // A bigger value makes the algorithm faster but less accurate.
const int OCTAVES = 3; // Determines the scale invariance of the algorithm.
const float PATTERN_SCALE = 1.0f;

const int TOTAL_PREDICTION_ATTEMPTS = 3; // How many attempts are made at the predicted image before others are tried.
const int REQUIRED_MATCHES = 4; // Minimum number of matches required to consider the object found.
const int MIN_KEYPOINTS_FOR_REDUCTION = 800; // How many keypoints there have to be before some are deleted to save on processing power.
const float MIN_RESPONSE_FACTOR = 0.6f; // Multiplied with the min response to form the limit above which keypoints are removed.
const int QUERY_IMAGE_REQUIRED_SIZE = 360; // Dimension to which the query images will be scaled.
const int TRAIN_IMAGE_REQUIRED_SIZE = 240; // Dimension to which the train images will be scaled.
const float MIN_DISTANCE_FACTOR = 3.0f; // Multiplied with the min distance to form the limit above which matches are removed.
const float KNN_MATCHES_SIMILARITY_FACTOR = 0.9f; // Similarity ratio between the 2 best matches of each descriptor above which the match is ignored.
const float REQUIRED_INLIERS = 0.65f; // Percentage of inliers in total matches to consider the object found.
const double RANSAC_THRESHOLD = 3.0; // Threshold used in the RANSAC algorithm.

// Train image.
std::vector<Mat> m_trainImages;
std::vector<std::vector<KeyPoint> > m_trainImagesKeypoints;
std::vector<Mat> m_trainImagesDescriptors;
std::vector<int> m_imagePages;
int m_trainImageNum = 0; // Number of train images given.

// Query image.
std::vector<KeyPoint> m_queryImageKeypoints;
Mat m_queryImageDescriptors;

// Detection.
Ptr<DescriptorMatcher> m_matcher;
Ptr<Feature2D> m_briskDetector;
Mat m_H; // Homography matrix.
int m_predictedImage = -1; // Which image is expected to appear next. Used for optimization.
int m_predictionCount = TOTAL_PREDICTION_ATTEMPTS; // How many times only the predicted image will be searched for.

bool m_initialized = false; // Whether the library is initialized.

// Function prototypes.
bool doMatching(int imageIndex);
Mat getCameraMatrix(int width, int height);
Mat getRot(const std::vector<Point2f>& imagePoints, const std::vector<Point2f>& objectPoints, int width, int height);

EXTERNAL_FUNC addImage(void* trainImageInput, int width, int height, int page) {

	// Create a new mat object to store the input data.
	Mat newImage(height, width, CV_8UC4);

	// Copy the data to the mat.
	memcpy(newImage.data, trainImageInput, width * height * 4);

	// Turn the mat to grayscale.
	cvtColor(newImage, newImage, CV_RGBA2GRAY);

	// Resize the input image.
	int smallSide = newImage.cols > newImage.rows ? newImage.rows : newImage.cols;
	float scaleFactor = (float)TRAIN_IMAGE_REQUIRED_SIZE / smallSide;
	resize(newImage, newImage, Size((int)(width * scaleFactor), (int)(height * scaleFactor)));

	GaussianBlur(newImage, newImage, Size(3, 3), 0);

	// Save new image data.
	m_trainImages.push_back(newImage);
	m_imagePages.push_back(page);
	m_trainImageNum++;
}

EXTERNAL_FUNC initScan() {
	m_matcher = DescriptorMatcher::create("BruteForce-Hamming");
	m_briskDetector = BRISK::create(THRESHOLD, OCTAVES, PATTERN_SCALE);

	for (int i = 0; i < m_trainImageNum; i++) {

		std::vector<KeyPoint> currentImageKeypoints;

		m_briskDetector->detect(m_trainImages[i], currentImageKeypoints);

#pragma region KeypointReduction
		if (currentImageKeypoints.size() > MIN_KEYPOINTS_FOR_REDUCTION) {
			int num = currentImageKeypoints.size();
			float max = currentImageKeypoints[0].response;

			// Find the max keypoint response.
			for (int j = 1; j < num; j++) {
				if (max < currentImageKeypoints[j].response)
					max = currentImageKeypoints[j].response;
			}

			// Use the max value to filter out keypoints.
			std::vector<KeyPoint> temp;
			for (int j = 0; j < num; j++) {
				int response = currentImageKeypoints[j].response;
				if (max * MIN_RESPONSE_FACTOR < response)
					temp.push_back(currentImageKeypoints[j]);
			}
			currentImageKeypoints = std::move(temp);
		}
#pragma endregion

		Mat tempDescriptors;
		m_briskDetector->compute(m_trainImages[i], currentImageKeypoints, tempDescriptors);

		m_trainImagesKeypoints.push_back(std::move(currentImageKeypoints));
		m_trainImagesDescriptors.push_back(std::move(tempDescriptors));
	}
	m_initialized = true;
}

EXTERNAL_FUNC removeImages() {
	m_initialized = false;
	m_imagePages.clear();
	m_trainImageNum = 0;

	if (m_matcher != NULL) {
		m_matcher.release();
	}

	if (m_briskDetector)
		m_briskDetector.release();

	m_trainImages.clear();
	m_trainImagesKeypoints.clear();
	m_trainImagesDescriptors.clear();

	m_queryImageKeypoints.clear();
}

EXTERNAL_FUNC processImage(void* queryImage, int width, int height, int& foundPage, int& centerX, int& centerY, double* rotData) {
	foundPage = -1;

	if (!m_initialized)
		return;

	Mat webcamFrame = Mat(height, width, CV_8UC4);
	webcamFrame.data = (uchar*)queryImage;

	// Grayscale the input.
	Mat webcamFrameGray(webcamFrame.rows, webcamFrame.cols, CV_8UC1);
	cvtColor(webcamFrame, webcamFrameGray, CV_RGBA2GRAY);

	// Resize.
	int smallSide = webcamFrameGray.cols > webcamFrameGray.rows ? webcamFrameGray.rows : webcamFrameGray.cols;
	float scaleFactor = (float)QUERY_IMAGE_REQUIRED_SIZE / smallSide;
	resize(webcamFrameGray, webcamFrameGray, Size((int)(width * scaleFactor), (int)(height * scaleFactor)));

	// Do the detection.
	m_briskDetector->detectAndCompute(webcamFrameGray, Mat(), m_queryImageKeypoints, m_queryImageDescriptors);

	if (m_queryImageKeypoints.size() == 0 || m_queryImageDescriptors.cols == 0) {
		return;
	}

	// Look for the predicted image first if there is one.
	int foundImage = -1;
	if (m_predictedImage != -1) {
		bool found = doMatching(m_predictedImage);
		if (found) {
			foundImage = m_predictedImage;
			m_predictionCount = TOTAL_PREDICTION_ATTEMPTS;
		}
		else {
			m_predictionCount--;
			if (m_predictionCount == 0) {
				m_predictedImage = -1;
			}
			return;
		}
	}

	// If the predicted image was not found, look for the other images.
	if (foundImage == -1) {
		for (int i = 0; i < m_trainImageNum; i++) {
			if (i == m_predictedImage)
				continue;
			bool found = doMatching(i);
			if (found) {
				foundImage = i;
				m_predictedImage = i;
				m_predictionCount = TOTAL_PREDICTION_ATTEMPTS;
				break;
			}
		}
	}

	if (foundImage != -1) {

		std::vector<Point2f> queryImageCorners(4);
		std::vector<Point2f> trainImageCorners(4);

		trainImageCorners[0] = Point2f(0, 0);
		trainImageCorners[1] = Point2f((float)m_trainImages[foundImage].cols, 0);
		trainImageCorners[2] = Point2f((float)m_trainImages[foundImage].cols, (float)m_trainImages[foundImage].rows);
		trainImageCorners[3] = Point2f(0, (float)m_trainImages[foundImage].rows);

		perspectiveTransform(trainImageCorners, queryImageCorners, m_H);

		Mat rotationMatrix = getRot(queryImageCorners, trainImageCorners, webcamFrameGray.cols, webcamFrameGray.rows);

		// Copy the rotation data to the return pointer.
		memcpy(rotData, rotationMatrix.data, 9 * 8);

		// Scale the points to fit the image and calculate center of mass for the query image corners.
		float xScale = (float)width / webcamFrameGray.cols;
		float yScale = (float)height / webcamFrameGray.rows;
		int sumX = 0, sumY = 0;
		for (int i = 0; i < 4; i++) {
			queryImageCorners[i].x *= xScale;
			queryImageCorners[i].y *= yScale;

			sumX += queryImageCorners[i].x;
			sumY += queryImageCorners[i].y;
		}
		centerX = sumX / 4;
		centerY = sumY / 4;

		foundPage = m_imagePages[foundImage];
	}
}

// Generate the default camera matrix.
Mat getCameraMatrix(int width, int height) {

	Mat mat = Mat(3, 3, CV_64F);
	mat.at<double>(0, 0) = width;
	mat.at<double>(0, 1) = 0;
	mat.at<double>(0, 2) = width / 2.0;
	mat.at<double>(1, 0) = 0;
	mat.at<double>(1, 1) = width;
	mat.at<double>(1, 2) = height / 2.0;
	mat.at<double>(2, 0) = 0;
	mat.at<double>(2, 1) = 0;
	mat.at<double>(2, 2) = 1;
	return mat;
}

Mat getRot(const std::vector<Point2f>& imagePoints, const std::vector<Point2f>& objectPoints, int width, int height) {

	std::vector<Point3f> obj;
	for (int i = 0; i < objectPoints.size(); i++)
		obj.push_back(Point3f(objectPoints[i]));

	Mat R(3, 1, CV_64F);
	Mat T(3, 1, CV_64F);

	Mat distCoef = Mat::zeros(4, 1, CV_64F);
	solvePnP(obj, imagePoints, getCameraMatrix(width, height), distCoef, R, T, false, SOLVEPNP_EPNP);

	// Use the Rodrigues function to convert the rotation vector to the rotation matrix.
	Mat rotMatrix(3, 3, CV_64F);
	Rodrigues(R, rotMatrix);

	return rotMatrix;
}

bool doMatching(int imageIndex) {
	std::vector<std::vector<DMatch> > tempMatches;

	m_matcher->knnMatch(m_trainImagesDescriptors[imageIndex], m_queryImageDescriptors, tempMatches, 2);

	std::vector<DMatch> goodMatches;

	// --- Filter matches ---
	// Filter 1 
	for (size_t j = 0; j < tempMatches.size(); j++) {

		// If there was only one match, then add it to the vector.
		if (tempMatches[j].size() == 1) {
			goodMatches.push_back(tempMatches[j][0]);
			continue;
		}

		// Check the similarity between the two best matches and only add if it isn't greater than a threshold.
		float distance1 = tempMatches[j][0].distance;
		float distance2 = tempMatches[j][1].distance;

		if (distance2 * KNN_MATCHES_SIMILARITY_FACTOR > distance1) {
			goodMatches.push_back(tempMatches[j][0]);
		}
	}
	if (goodMatches.size() == 0)
		return false;

	// Filter 2
	std::sort(goodMatches.begin(), goodMatches.end());
	float minDistance = goodMatches[0].distance * MIN_DISTANCE_FACTOR;
	for (size_t j = 0; j < goodMatches.size(); j++) {
		if (goodMatches[j].distance > minDistance) {
			goodMatches.erase(goodMatches.begin() + j, goodMatches.end());
			break;
		}
	}
	// --- Filters end ---

	if (goodMatches.size() < REQUIRED_MATCHES) {
		return false;
	}

	// Calculate homography.
	std::vector<Point2f> points1, points2;
	for (size_t j = 0; j < goodMatches.size(); j++) {
		points1.push_back(m_trainImagesKeypoints[imageIndex][goodMatches[j].queryIdx].pt);
		points2.push_back(m_queryImageKeypoints[goodMatches[j].trainIdx].pt);
	}

	Mat mask;
	Mat Htemp = findHomography(points1, points2, RANSAC, RANSAC_THRESHOLD, mask);

	if (!Htemp.empty()) {

		unsigned int inliers = 0;
		for (int r = 0; r < mask.rows; r++) {
			if ((unsigned int)mask.at<uchar>(r, 0))
				inliers++;
		}

		float inlierPercentage = (float)inliers / mask.rows;

		if (inlierPercentage >= REQUIRED_INLIERS) {
			// Image found.
			m_H = std::move(Htemp);
			return true;
		}
		return false;
	}
	else {
		return false;
	}
}