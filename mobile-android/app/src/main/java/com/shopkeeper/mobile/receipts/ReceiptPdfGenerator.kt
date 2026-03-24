package com.shopkeeper.mobile.receipts

import android.content.Context
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import com.shopkeeper.mobile.ui.components.PaymentMethodOption
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import kotlin.math.max

class ReceiptPdfGenerator(private val context: Context) {
    fun generateCustomerReceipt(receipt: ReceiptSourcePayload, version: String): File {
        val file = receiptFile(receipt, version, ReceiptKinds.Customer)
        val document = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(384, 760, 1).create()
        val page = document.startPage(pageInfo)
        val canvas = page.canvas
        var y = 36f

        val titlePaint = Paint().apply { textSize = 16f; typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD) }
        val headingPaint = Paint().apply { textSize = 12f; typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD) }
        val bodyPaint = Paint().apply { textSize = 11f }
        val smallPaint = Paint().apply { textSize = 10f }

        fun line(text: String, paint: Paint = bodyPaint, gap: Float = 20f) {
            canvas.drawText(text, 20f, y, paint)
            y += gap
        }

        line(receipt.shopName, titlePaint, 24f)
        line("Customer Receipt", headingPaint)
        line("Sale: ${receipt.saleNumber}")
        line("Cashier: ${receipt.cashierName}")
        line("Date: ${formatTimestamp(receipt.createdAtUtcIso)}")
        line("Customer: ${receipt.customerName?.ifBlank { "Walk-in Customer" } ?: "Walk-in Customer"}")
        y += 6f
        line("Items", headingPaint)
        receipt.lines.forEach { item ->
            line("${item.productName}  x${item.quantity}", bodyPaint)
            line("NGN ${"%.2f".format(item.unitPrice)}  ->  NGN ${"%.2f".format(item.lineTotal)}", smallPaint, 16f)
        }
        y += 6f
        line("Subtotal: NGN ${"%.2f".format(receipt.subtotal)}")
        line("Discount: NGN ${"%.2f".format(receipt.discountAmount)}")
        line("VAT: NGN ${"%.2f".format(receipt.vatAmount)}")
        line("Total: NGN ${"%.2f".format(receipt.totalAmount)}", headingPaint)
        line("Paid: NGN ${"%.2f".format(receipt.paidAmount)}")
        line("Outstanding: NGN ${"%.2f".format(receipt.outstandingAmount)}")

        val totalCashAmount = receipt.payments.filter { it.methodCode == 1 }.sumOf { it.amount }
        val hasCash = totalCashAmount > 0
        val allCashTenderedPresent = receipt.payments.filter { it.methodCode == 1 }.all { it.cashTendered != null }
        if (hasCash) {
            line("Total Cash: NGN ${"%.2f".format(totalCashAmount)}")
            if (allCashTenderedPresent) {
                val totalCashTendered = receipt.payments.filter { it.methodCode == 1 }.sumOf { it.cashTendered ?: 0.0 }
                val changeDue = max(0.0, totalCashTendered - totalCashAmount)
                line("Cash Tendered: NGN ${"%.2f".format(totalCashTendered)}")
                line("Change Due: NGN ${"%.2f".format(changeDue)}", headingPaint)
            }
        }

        y += 8f
        line("Thank you for your purchase.", headingPaint)

        document.finishPage(page)
        file.outputStream().use { document.writeTo(it) }
        document.close()
        return file
    }

    fun generateOwnerReceipt(receipt: ReceiptSourcePayload, version: String): File {
        val file = receiptFile(receipt, version, ReceiptKinds.Owner)
        val document = PdfDocument()
        val pageInfo = PdfDocument.PageInfo.Builder(595, 842, 1).create()
        val page = document.startPage(pageInfo)
        val canvas = page.canvas
        var y = 42f

        val titlePaint = Paint().apply { textSize = 18f; typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD) }
        val headingPaint = Paint().apply { textSize = 13f; typeface = Typeface.create(Typeface.DEFAULT, Typeface.BOLD) }
        val bodyPaint = Paint().apply { textSize = 11f }

        fun line(text: String, x: Float = 28f, paint: Paint = bodyPaint, gap: Float = 20f) {
            canvas.drawText(text, x, y, paint)
            y += gap
        }

        line(receipt.shopName, paint = titlePaint, gap = 26f)
        line("INTERNAL / CONFIDENTIAL", paint = headingPaint)
        line("Sale: ${receipt.saleNumber}")
        line("Cashier: ${receipt.cashierName}")
        line("Date: ${formatTimestamp(receipt.createdAtUtcIso)}")
        line("Customer: ${receipt.customerName?.ifBlank { "Walk-in Customer" } ?: "Walk-in Customer"}")
        y += 8f
        line("Items", paint = headingPaint)

        receipt.lines.forEach { item ->
            val lineProfit = (item.unitPrice - item.costPrice) * item.quantity
            line("${item.productName} x${item.quantity}")
            line(
                "Sell ${"%.2f".format(item.unitPrice)} | Cost ${"%.2f".format(item.costPrice)} | Total ${"%.2f".format(item.lineTotal)} | Profit ${"%.2f".format(lineProfit)}",
                x = 44f,
                gap = 18f
            )
        }

        val totalCogs = receipt.lines.sumOf { it.costPrice * it.quantity }
        val grossProfit = receipt.totalAmount - totalCogs
        val grossMarginPct = if (receipt.totalAmount <= 0.0) 0.0 else (grossProfit / receipt.totalAmount) * 100.0
        val totalCashAmount = receipt.payments.filter { it.methodCode == 1 }.sumOf { it.amount }
        val allCashTenderedPresent = receipt.payments.filter { it.methodCode == 1 }.all { it.cashTendered != null }

        y += 8f
        line("Subtotal: NGN ${"%.2f".format(receipt.subtotal)}")
        line("Discount: NGN ${"%.2f".format(receipt.discountAmount)}")
        line("VAT: NGN ${"%.2f".format(receipt.vatAmount)}")
        line("Total: NGN ${"%.2f".format(receipt.totalAmount)}", paint = headingPaint)
        line("Paid: NGN ${"%.2f".format(receipt.paidAmount)}")
        line("Outstanding: NGN ${"%.2f".format(receipt.outstandingAmount)}")
        line("COGS: NGN ${"%.2f".format(totalCogs)}")
        line("Gross Profit: NGN ${"%.2f".format(grossProfit)}")
        line("Gross Margin: ${"%.2f".format(grossMarginPct)}%")
        if (totalCashAmount > 0) {
            line("Total Cash: NGN ${"%.2f".format(totalCashAmount)}")
            if (allCashTenderedPresent) {
                val totalCashTendered = receipt.payments.filter { it.methodCode == 1 }.sumOf { it.cashTendered ?: 0.0 }
                line("Cash Tendered: NGN ${"%.2f".format(totalCashTendered)}")
                line("Change Due: NGN ${"%.2f".format(max(0.0, totalCashTendered - totalCashAmount))}")
            }
        }

        y += 8f
        line("Payments", paint = headingPaint)
        receipt.payments.forEach { payment ->
            line("${paymentMethodLabel(payment.methodCode)} • NGN ${"%.2f".format(payment.amount)} • ${payment.reference ?: "No reference"}")
        }

        document.finishPage(page)
        file.outputStream().use { document.writeTo(it) }
        document.close()
        return file
    }

    private fun receiptFile(receipt: ReceiptSourcePayload, version: String, kind: String): File {
        val dir = File(context.cacheDir, "receipts").apply { mkdirs() }
        val anchor = if (version == ReceiptVersions.Canonical) {
            receipt.saleId ?: receipt.localSaleId
        } else {
            receipt.localSaleId
        }
        return File(dir, "$anchor-$version-$kind.pdf")
    }

    private fun formatTimestamp(iso: String): String {
        return runCatching {
            Instant.parse(iso)
                .atZone(ZoneId.systemDefault())
                .format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm"))
        }.getOrElse { iso }
    }

    private fun paymentMethodLabel(code: Int): String {
        return PaymentMethodOption.fromCode(code).label
    }
}
