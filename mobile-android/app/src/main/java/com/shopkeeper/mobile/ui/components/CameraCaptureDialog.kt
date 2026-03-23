package com.shopkeeper.mobile.ui.components

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.ui.window.DialogProperties
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import java.io.File

enum class CameraCaptureMode {
    ScanText,
    CapturePhoto
}

@Composable
fun CameraCaptureDialog(
    title: String,
    subtitle: String,
    mode: CameraCaptureMode,
    onDismissRequest: () -> Unit,
    onTextCaptured: (String) -> Unit = {},
    onPhotoCaptured: (Uri) -> Unit = {},
    onError: (String) -> Unit = {}
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val mainExecutor = remember(context) { ContextCompat.getMainExecutor(context) }
    val previewView = remember(context) {
        PreviewView(context).apply {
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            scaleType = PreviewView.ScaleType.FILL_CENTER
        }
    }
    val recognizer = remember { TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS) }

    var hasPermission by remember {
        mutableStateOf(
            ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED
        )
    }
    var imageCapture by remember { mutableStateOf<ImageCapture?>(null) }
    var isBindingCamera by remember { mutableStateOf(false) }
    var isCapturing by remember { mutableStateOf(false) }
    var inlineStatus by remember { mutableStateOf("") }

    val permissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        hasPermission = granted
        if (!granted) {
            inlineStatus = "Camera permission denied. Allow camera access to scan or capture images."
            onError(inlineStatus)
        }
    }

    LaunchedEffect(Unit) {
        if (!hasPermission) {
            permissionLauncher.launch(Manifest.permission.CAMERA)
        }
    }

    DisposableEffect(hasPermission) {
        if (!hasPermission) {
            imageCapture = null
            onDispose { }
        } else {
            val future = ProcessCameraProvider.getInstance(context)
            val listener = Runnable {
                runCatching {
                    isBindingCamera = true
                    val cameraProvider = future.get()
                    val preview = Preview.Builder().build().also {
                        it.setSurfaceProvider(previewView.surfaceProvider)
                    }
                    val capture = ImageCapture.Builder()
                        .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                        .build()

                    cameraProvider.unbindAll()
                    cameraProvider.bindToLifecycle(
                        lifecycleOwner,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        capture
                    )
                    imageCapture = capture
                    inlineStatus = ""
                }.onFailure { ex ->
                    imageCapture = null
                    inlineStatus = "Failed to start camera: ${ex.message.orEmpty()}"
                    onError(inlineStatus)
                }
                isBindingCamera = false
            }

            future.addListener(listener, mainExecutor)

            onDispose {
                runCatching {
                    if (future.isDone) {
                        future.get().unbindAll()
                    }
                }
                imageCapture = null
                isBindingCamera = false
            }
        }
    }

    fun captureFrame() {
        val capture = imageCapture
        if (capture == null) {
            inlineStatus = "Camera is not ready yet."
            return
        }

        val outputDir = File(context.cacheDir, "camera-captures").apply { mkdirs() }
        val outputFile = File(outputDir, "capture-${System.currentTimeMillis()}.jpg")
        val outputOptions = ImageCapture.OutputFileOptions.Builder(outputFile).build()

        isCapturing = true
        inlineStatus = if (mode == CameraCaptureMode.ScanText) {
            "Capturing frame for OCR..."
        } else {
            "Capturing photo..."
        }

        capture.takePicture(
            outputOptions,
            mainExecutor,
            object : ImageCapture.OnImageSavedCallback {
                override fun onImageSaved(outputFileResults: ImageCapture.OutputFileResults) {
                    val uri = outputFileResults.savedUri ?: Uri.fromFile(outputFile)
                    if (mode == CameraCaptureMode.CapturePhoto) {
                        isCapturing = false
                        onPhotoCaptured(uri)
                        onDismissRequest()
                        return
                    }

                    runCatching { InputImage.fromFilePath(context, uri) }
                        .onSuccess { image ->
                            recognizer.process(image)
                                .addOnSuccessListener { result ->
                                    isCapturing = false
                                    val text = result.text.trim()
                                    if (text.isBlank()) {
                                        inlineStatus = "No text detected. Move closer and try again."
                                        onError(inlineStatus)
                                    } else {
                                        onTextCaptured(text)
                                        runCatching { outputFile.delete() }
                                        onDismissRequest()
                                    }
                                }
                                .addOnFailureListener { ex ->
                                    isCapturing = false
                                    inlineStatus = "OCR failed: ${ex.message.orEmpty()}"
                                    onError(inlineStatus)
                                }
                        }
                        .onFailure { ex ->
                            isCapturing = false
                            inlineStatus = "Could not read captured image: ${ex.message.orEmpty()}"
                            onError(inlineStatus)
                        }
                }

                override fun onError(exception: ImageCaptureException) {
                    isCapturing = false
                    inlineStatus = "Camera capture failed: ${exception.message.orEmpty()}"
                    onError(inlineStatus)
                }
            }
        )
    }

    Dialog(
        onDismissRequest = {
            if (!isCapturing) {
                onDismissRequest()
            }
        },
        properties = DialogProperties(usePlatformDefaultWidth = false)
    ) {
        Surface(
            modifier = Modifier.fillMaxSize(),
            color = MaterialTheme.colorScheme.background
        ) {
            Column(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                Text(
                    title,
                    style = MaterialTheme.typography.headlineSmall,
                    color = MaterialTheme.colorScheme.onBackground
                )
                Text(
                    subtitle,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )

                Box(
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(24.dp)),
                    contentAlignment = Alignment.Center
                ) {
                    if (hasPermission) {
                        AndroidView(
                            factory = { previewView },
                            modifier = Modifier.fillMaxSize()
                        )

                        if (isBindingCamera || isCapturing) {
                            Box(
                                modifier = Modifier
                                    .fillMaxSize()
                                    .background(MaterialTheme.colorScheme.scrim.copy(alpha = 0.25f)),
                                contentAlignment = Alignment.Center
                            ) {
                                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                    CircularProgressIndicator()
                                    Text(
                                        if (isCapturing) "Processing capture..." else "Starting camera...",
                                        modifier = Modifier.padding(top = 12.dp),
                                        color = MaterialTheme.colorScheme.onBackground
                                    )
                                }
                            }
                        }
                    } else {
                        Column(
                            modifier = Modifier
                                .fillMaxSize()
                                .padding(24.dp),
                            verticalArrangement = Arrangement.Center,
                            horizontalAlignment = Alignment.CenterHorizontally
                        ) {
                            Text(
                                "Camera access is required for this action.",
                                color = MaterialTheme.colorScheme.onBackground,
                                style = MaterialTheme.typography.titleMedium
                            )
                            Text(
                                "Grant permission and try again.",
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                modifier = Modifier.padding(top = 8.dp)
                            )
                        }
                    }
                }

                if (inlineStatus.isNotBlank()) {
                    StatusBanner(inlineStatus)
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    SoftButton(
                        text = "Cancel",
                        onClick = onDismissRequest,
                        modifier = Modifier.weight(1f)
                    )
                    if (hasPermission) {
                        BrickButton(
                            text = if (mode == CameraCaptureMode.ScanText) "Capture & Scan" else "Capture Photo",
                            onClick = { captureFrame() },
                            modifier = Modifier.weight(1f)
                        )
                    } else {
                        BrickButton(
                            text = "Grant Access",
                            onClick = { permissionLauncher.launch(Manifest.permission.CAMERA) },
                            modifier = Modifier.weight(1f)
                        )
                    }
                }
            }
        }
    }
}
