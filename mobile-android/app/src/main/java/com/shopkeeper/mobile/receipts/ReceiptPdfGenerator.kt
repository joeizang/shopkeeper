package com.shopkeeper.mobile.receipts

import android.content.Context
import android.graphics.Paint
import android.graphics.pdf.PdfDocument
import java.io.File

class ReceiptPdfGenerator(private val context: Context) {
    fun generateSampleReceipt(
        saleNumber: String,
        customerName: String,
        totalAmount: Double,
        paymentReference: String?
    ): File {
        val document = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(300, 600, 1).create()
        val page = document.startPage(pageInfo)
        val canvas = page.canvas
        val paint = Paint().apply { textSize = 12f }

        canvas.drawText("Shopkeeper Receipt", 16f, 30f, paint)
        canvas.drawText("Sale: $saleNumber", 16f, 55f, paint)
        canvas.drawText("Customer: ${customerName.ifBlank { "Walk-in" }}", 16f, 80f, paint)
        canvas.drawText("Total: NGN %.2f".format(totalAmount), 16f, 105f, paint)
        if (!paymentReference.isNullOrBlank()) {
            canvas.drawText("Reference: $paymentReference", 16f, 130f, paint)
        }

        document.finishPage(page)

        val dir = File(context.cacheDir, "receipts").apply { mkdirs() }
        val file = File(dir, "receipt-$saleNumber.pdf")
        file.outputStream().use { document.writeTo(it) }
        document.close()

        return file
    }
}
