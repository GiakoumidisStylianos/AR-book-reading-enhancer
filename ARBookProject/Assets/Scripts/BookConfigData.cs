using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

public class BookConfigData {

	// Singleton instance.
	private static BookConfigData m_instance = null;

	private string m_bookTitle;
	private int m_pages;
	private int m_imageElements, m_soundElements, m_videoElements;

	private string[] m_ARImagesPaths;
	private string[] m_pagesPaths;
	private string[] m_audioPaths;
	private string[] m_videoPaths;

	private Dictionary<int, int> m_pageToImagePathIndex;
	private Dictionary<int, int> m_pageToAudioPathIndex;
	private Dictionary<int, int> m_pageToVideoPathIndex;

	public static BookConfigData Instance {
		get {
			if (m_instance == null)
				m_instance = new BookConfigData();
			return m_instance;
		}
	}

	public string[] PagePaths {
		get {
			string[] tmp = new string[m_pagesPaths.Length];
			m_pagesPaths.CopyTo(tmp, 0);
			return tmp;
		}
	}

	public string BookTitle => m_bookTitle;

	public int Pages => m_pages;

	public int ImageElements => m_imageElements;

	public int SoundElements => m_soundElements;

	public int VideoElements => m_videoElements;
	
	private BookConfigData() {

		m_bookTitle = "";
		m_pages = 0;
		m_imageElements = m_soundElements = m_videoElements = 0;

		m_ARImagesPaths = new string[0];
		m_pagesPaths = new string[0];
		m_audioPaths = new string[0];
		m_videoPaths = new string[0];

		m_pageToImagePathIndex = new Dictionary<int, int>();
		m_pageToAudioPathIndex = new Dictionary<int, int>();
		m_pageToVideoPathIndex = new Dictionary<int, int>();
	}

	public bool ParseDirectory(string rootPath) {

		// Reset values.
		Clear();

		// Check if a valid directory was given.
		if (!Directory.Exists(rootPath)) {
			return false;
		}

		// Create temporary variables to hold data.
		List<string> arImagePaths = new List<string>();
		List<string> pagePaths = new List<string>();
		List<string> audioPaths = new List<string>();
		List<string> videoPaths = new List<string>();

		// Get the files in root folder.
		string[] files = Directory.GetFiles(rootPath);

		foreach (string path in files) {

			// Filename without path.
			string filename = path.Substring(path.LastIndexOf('/') + 1);

			// Page file.
			if (filename.StartsWith("page")) {
				pagePaths.Add(path);
			}

			// Config file.
			else if (filename == "config.txt") {

				// Read the file.
				string configFileContents = "";
				using (StreamReader sr = new StreamReader(path)) {
					configFileContents = sr.ReadToEnd();
				}

				// Get the title of the book.
				string titleLinePattern = @"title:\s*[\w\s]+\n";
				Match titleLineMatch = Regex.Match(configFileContents, titleLinePattern);
				if (titleLineMatch.Success) {
					string line = titleLineMatch.Value;
					int index = line.IndexOf(':');
					m_bookTitle = line.Substring(index + 1, line.Length - index - 1).Trim();
				}
				else {
					return false;
				}

				// Regex for parsing the file.
				string linePattern = @"page\s\d:([\r\n]*\s*\w+:\s*[\w/\s]+\.\w+)+"; // Matches the entire entry for a page.
				string mediaPattern = @"(image:|sound:|video:)[\w/\s]+\.\w+"; // Matches a media of the page.
				string pathPattern = @"[\w/\s]+\.\w+"; // The path of a media of the page.

				Match lineMatch = Regex.Match(configFileContents, linePattern);
				while (lineMatch.Success) {
					m_pages++;
					string page = lineMatch.Value;

					// Find the page number.
					int pageDigits = 0;
					while(true) {
						char c = page[5 + pageDigits];
						if (char.IsDigit(c))
							pageDigits++;
						else
							break;
					}
					int currentPage = Convert.ToInt32(page.Substring(5, pageDigits));

					Match matchMedia = Regex.Match(page, mediaPattern);
					while (matchMedia.Success) {
						string media = matchMedia.Value;
						Match matchPath = Regex.Match(media, pathPattern);
						if (matchPath.Success) {
							string mediaPath = rootPath + '/' + matchPath.Value;

							if (File.Exists(mediaPath)) {
								if (media.Contains("image:")) {
									m_imageElements++;
									arImagePaths.Add(mediaPath);
									m_pageToImagePathIndex[currentPage] = arImagePaths.Count - 1;

								}
								else if (media.Contains("sound:")) {
									m_soundElements++;
									audioPaths.Add(mediaPath);
									m_pageToAudioPathIndex[currentPage] = audioPaths.Count - 1;
								}
								else if (media.Contains("video:")) {
									m_videoElements++;
									videoPaths.Add(mediaPath);
									m_pageToVideoPathIndex[currentPage] = videoPaths.Count - 1;
								}
							}
						}
						matchMedia = matchMedia.NextMatch();
					}
					lineMatch = lineMatch.NextMatch();
				}
			}
		}

		// Save the data to member variables.
		m_ARImagesPaths = arImagePaths.ToArray();
		m_pagesPaths = pagePaths.ToArray();
		m_audioPaths = audioPaths.ToArray();
		m_videoPaths = videoPaths.ToArray();

		return true;
	}

	public void Clear() {
		// Reset values.
		m_pagesPaths = new string[0];
		m_ARImagesPaths = new string[0];
		m_audioPaths = new string[0];
		m_videoPaths = new string[0];

		m_pageToImagePathIndex.Clear();
		m_pageToAudioPathIndex.Clear();
		m_pageToVideoPathIndex.Clear();

		m_bookTitle = "";
		m_pages = 0;
		m_imageElements = m_soundElements = m_videoElements = 0;
	}

	public string GetARimagePath(int page) {

		// If this page does not have an image to show, return null.
		if (!m_pageToImagePathIndex.ContainsKey(page))
			return null;

		// Look at the dictionary for the correct index in the array.
		int index = m_pageToImagePathIndex[page];
		return m_ARImagesPaths[index];
	}

	public string GetAudioPath(int page) {

		// If this page does not have an audio to play, return null.
		if (!m_pageToAudioPathIndex.ContainsKey(page))
			return null;

		// Look at the dictionary for the correct index in the array.
		int index = m_pageToAudioPathIndex[page];
		return m_audioPaths[index];
	}

	public string GetVideoPath(int page) {

		// If this page does not have a video to play, return null.
		if (!m_pageToVideoPathIndex.ContainsKey(page))
			return null;

		// Look at the dictionary for the correct index in the array.
		int index = m_pageToVideoPathIndex[page];
		return m_videoPaths[index];
	}

}