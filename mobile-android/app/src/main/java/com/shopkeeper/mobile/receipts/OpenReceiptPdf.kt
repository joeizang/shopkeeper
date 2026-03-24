package com.shopkeeper.mobile.receipts

import android.content.Context
import android.content.Intent
import androidx.core.content.FileProvider
import java.io.File

fun openReceiptPdf(context: Context, file: File) {
    val uri = FileProvider.getUriForFile(
        context,
        "${context.packageName}.fileprovider",
        file
    )

    val intent = Intent(Intent.ACTION_VIEW).apply {
        setDataAndType(uri, "application/pdf")
        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    }

    context.startActivity(Intent.createChooser(intent, "Open receipt"))
}
