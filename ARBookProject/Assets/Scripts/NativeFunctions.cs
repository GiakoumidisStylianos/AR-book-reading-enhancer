using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public static class NativeFunctions {

    private const string libraryName = "OpenCVAndroidLibrary";

	// Library methods.
	[DllImport(libraryName)]
	public unsafe static extern void initScan();

	[DllImport(libraryName)]
	public unsafe static extern void removeImages();

	[DllImport(libraryName)]
	public unsafe static extern void addImage(void* queryImage, int width, int height, int page);

	[DllImport(libraryName)]
	public unsafe static extern void processImage(void* trainImage, int width, int height, ref int detectedPage, ref int foundPageX, ref int foundPageY, double* outRotMatrix);
}
