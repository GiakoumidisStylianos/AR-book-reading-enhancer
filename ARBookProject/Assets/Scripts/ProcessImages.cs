using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

public class ProcessImages : MonoBehaviour {

	[SerializeField] private RawImage m_background; // Background of the canvas.
	[SerializeField] private RectTransform m_canvas; // The canvas' transform.
	[SerializeField] private RawImage m_contentImage; // The image containing the AR video and image elements.

	private AudioSource m_audioSource; // Play the audio.
	private WebCamTexture m_camTexture; // Texture onto which the camera frame is drawn.
	private Color32[] m_pixels; // Array to hold the camera frame's pixel data.
	private Camera m_mainCamera;

	private VideoPlayer m_videoPlayer; // Used to play video.
	private RenderTexture m_renderTexture; // The AR video is drawn on this texture.
	private Texture2D m_imageTexture; // The AR image is drawn on this texture.

	private BookConfigData configData;

	private bool m_readyToProcess; // True if the initialization was successful.

	// Last audio and video file played so that they are not repeated.
	private string m_lastAudioFile;
	private string m_lastVideoFile;

	private void Start() {

		configData = BookConfigData.Instance;

		m_readyToProcess = false;
		m_lastAudioFile = m_lastVideoFile = "";

		m_mainCamera = FindObjectOfType<Camera>();
		m_videoPlayer = FindObjectOfType<VideoPlayer>();
		m_audioSource = GetComponent<AudioSource>();

		m_imageTexture = new Texture2D(1, 1);

		m_renderTexture = new RenderTexture(256, 256, 24);
		m_videoPlayer.targetTexture = m_renderTexture;

		// Make the device not go to sleep on idle.
		Screen.sleepTimeout = SleepTimeout.NeverSleep;

		// Get the camera devices.
		WebCamDevice[] devices = WebCamTexture.devices;
		if (devices.Length == 0) {
			Debug.Log("No camera found");
			return;
		}

		// Start the camera recording.
		m_camTexture = new WebCamTexture(devices[0].name, Screen.width, Screen.height);
		m_camTexture.Play();
		m_background.texture = m_camTexture;

		// Create space for the library input and output pixels.
		int totalPixels = m_camTexture.width * m_camTexture.height;
		m_pixels = new Color32[totalPixels];

		// Set the aspect ratio.
		AspectRatioFitter fit = m_background.GetComponent<AspectRatioFitter>();
		float ratio = (float)m_canvas.rect.width / (float)m_canvas.rect.height;
		fit.aspectRatio = ratio;

		// Get the paths for the images to be scanned.
		string[] resourceNames = configData.PagePaths;

		for (int i = 0; i < resourceNames.Length; i++) {

			byte[] fileData = File.ReadAllBytes(resourceNames[i]);
			int page = GetPageNumberFromPath(resourceNames[i]);

			// Load the image into a Texture2D object.
			Texture2D image = new Texture2D(1, 1);
			image.LoadImage(fileData); // This will automatically resize the texture dimensions.

			// Get the pixel data of the image.
			Color32[] inPixels = image.GetPixels32();

			// Send the pixel data to the library to be scanned.
			unsafe {
				fixed (Color32* p1 = inPixels) {
					NativeFunctions.addImage(p1, image.width, image.height, page);
				}
			}
		}

		// Make the library to initiate the scan for all the target images.
		NativeFunctions.initScan();

		// The library is now ready.
		m_readyToProcess = true;
	}

	void Update() {

		// Respond to the back/esc button.
		if (Input.GetKeyDown(KeyCode.Escape)) {
			GoBack();
			return;
		}

		// Do not continue if the library is not ready to process images or if no camera was detected on the device.
		if (!m_readyToProcess)
			return;

		// Do not continue if the frame hasn't changed.
		if (!m_camTexture.didUpdateThisFrame)
			return;

		// Get pixel data from the camera.
		m_camTexture.GetPixels32(m_pixels);

		int detectedPage = -1;
		int centerx = 0;
		int centery = 0;

		double[] rotationMatrix = new double[9];

		// Process current frame.
		unsafe {
			fixed (Color32* p1 = m_pixels) {
				fixed (double* p2 = rotationMatrix) {
					NativeFunctions.processImage(p1, m_camTexture.width, m_camTexture.height, ref detectedPage, ref centerx, ref centery, p2);
				}
			}
		}

		if (detectedPage != -1) {

			// Check if there are digital elements for the detected page.
			string image = configData.GetARimagePath(detectedPage);
			string sound = configData.GetAudioPath(detectedPage);
			string video = configData.GetVideoPath(detectedPage);

			if (image != null) {

				// Stop the video if it was playing since they share the surface.
				if (m_videoPlayer.isPlaying)
					m_videoPlayer.Stop();
				m_lastVideoFile = "";

				// Set the texture.
				LoadTextureFromFile(m_imageTexture, image);
				m_contentImage.texture = m_imageTexture;
				
				Vector2 centerPoint = new Vector2(centerx, centery);
				Vector3 eulerAngles = GetEulerAngles(rotationMatrix);

				SetObjectTransform(m_contentImage.transform, centerPoint, eulerAngles);
				m_contentImage.gameObject.SetActive(true);
			}
			else if (video != null) {

				Vector2 centerPoint = new Vector2(centerx, centery);
				Vector3 eulerAngles = GetEulerAngles(rotationMatrix);

				if (video != m_lastVideoFile) {

					m_videoPlayer.url = video;
					m_contentImage.texture = m_renderTexture;
					m_videoPlayer.Play();

					// Remember this file so it is not played twice in a row.
					m_lastVideoFile = video;
				}

				SetObjectTransform(m_contentImage.transform, centerPoint, eulerAngles);
				m_contentImage.gameObject.SetActive(true);
			}
			
			if (sound != null) {

				if (!m_audioSource.isPlaying && sound != m_lastAudioFile) {
					AudioClip clip = LoadAudioClip(sound);
					if (clip != null) {
						m_audioSource.clip = clip;
						m_audioSource.Play();

						// Remember this file so it is not played twice in a row.
						m_lastAudioFile = sound;
					}
				}
			}
		}
		else {
			m_contentImage.gameObject.SetActive(false);
		}
	}

	public void BackButton() {
		GoBack();
	}

	private void GoBack() {
		m_readyToProcess = false;
		m_camTexture.Stop();
		NativeFunctions.removeImages();
		configData.Clear();
		SceneManager.LoadScene("BookSettings");
	}

	private Vector3 GetEulerAngles(double[] rotMatrix) {

		// Result variables.
		float thetaX = 0;
		float thetaY = 0;
		float thetaZ = 0;

		// Helper variables for the matrix.
		float r11 = (float)rotMatrix[0];
		float r21 = (float)rotMatrix[3];
		float r31 = (float)rotMatrix[6];
		float r32 = (float)rotMatrix[7];
		float r33 = (float)rotMatrix[8];

		// Calculations.
		thetaY = -Mathf.Asin(r31);
		thetaX = Mathf.Atan2(r32 / Mathf.Cos(thetaY), r33 / Mathf.Cos(thetaY));
		thetaZ = Mathf.Atan2(r21 / Mathf.Cos(thetaY), r11 / Mathf.Cos(thetaY));

		// Convert to degrees.
		thetaX *= Mathf.Rad2Deg;
		thetaY *= Mathf.Rad2Deg;
		thetaZ *= Mathf.Rad2Deg;

		thetaX *= 100; thetaX = Mathf.Round(thetaX); thetaX /= 100f;
		thetaY *= 100; thetaY = Mathf.Round(thetaY); thetaY /= 100f;
		thetaZ *= 100; thetaZ = Mathf.Round(thetaZ); thetaZ /= 100f;

		return new Vector3(thetaX, thetaY, thetaZ);
	}

	// Set the transform of the object based on the data received from the library.
	private void SetObjectTransform(Transform obj, Vector2 imgPos, Vector3 rot) {

		float canvasX = m_canvas.rect.width * (imgPos.x / m_camTexture.width);
		float canvasY = m_canvas.rect.height * (imgPos.y / m_camTexture.height);
		Vector3 canvasPos = new Vector3(canvasX, canvasY);

		Vector3 worldPos = m_mainCamera.ScreenToWorldPoint(canvasPos);
		worldPos.z = 0;

		obj.transform.position = worldPos;
		obj.transform.rotation = Quaternion.Euler(rot.x, rot.y, rot.z);
	}

	private int GetPageNumberFromPath(string path) {
		int numberIndex = path.LastIndexOf("page") + 4;
		int dotIndex = numberIndex;
		while (true) {
			if (char.IsDigit(path[dotIndex]))
				dotIndex++;
			else
				break;
		}
		return Convert.ToInt32(path.Substring(numberIndex, dotIndex - numberIndex));
	}

	private bool LoadTextureFromFile(Texture2D tex, string file) {

		byte[] imageData;
		try {
			imageData = File.ReadAllBytes(file);
		}
		catch (IOException) {
			return false;
		}

		tex.LoadImage(imageData);
		return true;
	}

	private AudioClip LoadAudioClip(string file) {

		if (!File.Exists(file))
			return null;

		AudioClip clip;
		string name;
		{
			int indx1 = file.LastIndexOf('\\') + 1;
			int indx2 = file.LastIndexOf('.');
			name = file.Substring(indx1, indx2 - indx1);
		}

		float[] samples;
		int fileSize;
		short channels;
		int sampleRate;

		using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read)) {
			BinaryReader reader = new BinaryReader(fs);

			// Read file size.
			fs.Seek(4, SeekOrigin.Begin);
			fileSize = reader.ReadInt32();
			samples = new float[(fileSize - 36) / 4]; // File size also accounts for remaining header, so it is subtracted by 36.

			// Read channels and sample rate.
			fs.Seek(22, SeekOrigin.Begin);
			channels = reader.ReadInt16();
			sampleRate = reader.ReadInt32();

			// Read samples.
			fs.Seek(44, SeekOrigin.Begin);
			for (int i = 0; i < samples.Length; i++)
				samples[i] = reader.ReadSingle();
		}

		clip = AudioClip.Create(name, samples.Length, channels, sampleRate, false);
		clip.SetData(samples, 0);

		return clip;
	}

#if UNITY_EDITOR
	// Not necessary when built, only for editor.
	private void OnApplicationQuit() {
		NativeFunctions.removeImages();
	}
#endif
}