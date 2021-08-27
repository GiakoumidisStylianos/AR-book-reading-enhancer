using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BookSettingsUIController : MonoBehaviour {

	[SerializeField] private Text m_txtConfigContent;
	[SerializeField] private InputField m_inptPath;
	[SerializeField] private Button m_btnBegin;

	private BookConfigData m_configData;

	private void Awake() {

		m_configData = BookConfigData.Instance;

		m_inptPath.text = GetAndroidExternalStoragePath();

	}

	private void Update() {
		if (Input.GetKeyDown(KeyCode.Escape)) {
			Application.Quit();
		}
	}

	private string GetAndroidExternalStoragePath() {
		string path = "";
		try {
			AndroidJavaClass jc = new AndroidJavaClass("android.os.Environment");
			path = jc.CallStatic<AndroidJavaObject>("getExternalStorageDirectory").Call<String>("getAbsolutePath");
			return path;
		}
		catch (Exception e) {
			Debug.Log(e.Message);
			return path;
		}
	}

	public void SetButton() {

		string dir = m_inptPath.text;
		bool success = m_configData.ParseDirectory(dir);
		string text = "";

		if (success) {

			// Set the text based on the config file.
			text += "Title: " + m_configData.BookTitle + '\n';
			text += "Pages with content: " + m_configData.Pages + '\n';
			text += "Pages with image content: " + m_configData.ImageElements + '\n';
			text += "Pages with audio content: " + m_configData.SoundElements + '\n';
			text += "Pages with video content: " + m_configData.VideoElements + '\n';

			// Enable the button.
			m_btnBegin.interactable = true;
		}
		else {
			text = "Error reading book data.";
			m_btnBegin.interactable = false;
		}
		m_txtConfigContent.text = text;
	}

	public void BeginButton() {
		SceneManager.LoadScene("PlayMode");
	}
}
