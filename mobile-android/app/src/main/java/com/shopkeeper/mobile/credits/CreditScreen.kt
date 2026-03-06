package com.shopkeeper.mobile.credits

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ExposedDropdownMenuBox
import androidx.compose.material3.ExposedDropdownMenuDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.CreditRepaymentInput
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.PaymentMethodDropdown
import com.shopkeeper.mobile.ui.components.PaymentMethodOption
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.StatusBanner
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CreditScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var creditSales by remember { mutableStateOf<List<com.shopkeeper.mobile.core.data.CreditSaleOption>>(emptyList()) }
    var saleId by rememberSaveable { mutableStateOf("") }
    var saleDropdownExpanded by rememberSaveable { mutableStateOf(false) }
    var repaymentAmount by rememberSaveable { mutableStateOf("") }
    var paymentMethodCode by rememberSaveable { mutableStateOf(PaymentMethodOption.Cash.code) }
    val paymentMethod = PaymentMethodOption.fromCode(paymentMethodCode)
    var reference by rememberSaveable { mutableStateOf("") }
    var notes by rememberSaveable { mutableStateOf("") }
    var status by rememberSaveable { mutableStateOf("") }

    fun refreshCredits() {
        scope.launch {
            creditSales = gateway.getOpenCreditSales()
            if (saleId.isBlank()) {
                saleId = creditSales.firstOrNull()?.saleId.orEmpty()
            }
        }
    }

    LaunchedEffect(Unit) {
        refreshCredits()
    }

    LaunchedEffect(saleId) {
        if (saleId.isBlank()) {
            reference = ""
            notes = ""
            paymentMethodCode = PaymentMethodOption.Cash.code
            return@LaunchedEffect
        }

        gateway.getCreditDetail(saleId)
            .onSuccess { detail ->
                val latestRepayment = detail.repayments.firstOrNull()
                reference = latestRepayment?.reference.orEmpty()
                notes = latestRepayment?.notes.orEmpty()
                paymentMethodCode = latestRepayment?.paymentMethodCode ?: PaymentMethodOption.Cash.code
            }
            .onFailure {
                reference = ""
                notes = ""
                paymentMethodCode = PaymentMethodOption.Cash.code
                status = "Could not load previous repayment details: ${it.message.orEmpty()}"
            }
    }

    ScreenColumn {
        ScreenHeader(
            title = "Credit repayments",
            subtitle = "Apply repayments to unsettled credit sales."
        )

        if (creditSales.isEmpty()) {
            StatusBanner("No open credit sales available yet.")
        } else {
            SectionTitle(
                title = "Select credit sale",
                subtitle = "Choose an unsettled sale. Fully settled sales no longer appear here."
            )
            ExposedDropdownMenuBox(
                expanded = saleDropdownExpanded,
                onExpandedChange = { saleDropdownExpanded = !saleDropdownExpanded },
                modifier = Modifier.fillMaxWidth()
            ) {
                val selected = creditSales.firstOrNull { it.saleId == saleId }
                val selectedText = selected?.let {
                    "${it.customerName} • ${it.itemSummary} • NGN ${"%.2f".format(it.outstandingAmount)}"
                }.orEmpty()

                OutlinedTextField(
                    value = selectedText,
                    onValueChange = {},
                    readOnly = true,
                    label = { Text("Credit Sale") },
                    trailingIcon = { ExposedDropdownMenuDefaults.TrailingIcon(expanded = saleDropdownExpanded) },
                    modifier = Modifier
                        .menuAnchor()
                        .fillMaxWidth()
                )

                ExposedDropdownMenu(
                    expanded = saleDropdownExpanded,
                    onDismissRequest = { saleDropdownExpanded = false }
                ) {
                    creditSales.forEach { option ->
                        DropdownMenuItem(
                            text = {
                                Text(
                                    "${option.customerName} • ${option.itemSummary} • NGN ${"%.2f".format(option.outstandingAmount)}",
                                    style = MaterialTheme.typography.bodySmall
                                )
                            },
                            onClick = {
                                saleId = option.saleId
                                saleDropdownExpanded = false
                            }
                        )
                    }
                }
            }
        }

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                SectionTitle(
                    title = "Repayment details",
                    subtitle = "The last saved reference and notes for this credit sale are loaded here so you can update them."
                )

                OutlinedTextField(
                    value = repaymentAmount,
                    onValueChange = { repaymentAmount = it },
                    label = { Text("Repayment Amount (NGN)") },
                    modifier = Modifier.fillMaxWidth()
                )

                PaymentMethodDropdown(
                    selected = paymentMethod,
                    onSelected = { paymentMethodCode = it.code },
                    modifier = Modifier.fillMaxWidth()
                )

                OutlinedTextField(
                    value = reference,
                    onValueChange = { reference = it },
                    label = { Text("Reference") },
                    modifier = Modifier.fillMaxWidth()
                )

                OutlinedTextField(
                    value = notes,
                    onValueChange = { notes = it },
                    label = { Text("Notes") },
                    minLines = 4,
                    maxLines = 8,
                    modifier = Modifier.fillMaxWidth()
                )

                BrickButton(text = "Apply Repayment", onClick = {
                    if (saleId.isBlank()) {
                        status = "Sale ID is required"
                        return@BrickButton
                    }

                    scope.launch {
                        val result = gateway.addCreditRepayment(
                            CreditRepaymentInput(
                                saleId = saleId,
                                amount = repaymentAmount.toDoubleOrNull() ?: 0.0,
                                paymentMethodCode = paymentMethod.code,
                                reference = reference.ifBlank { null },
                                notes = notes.ifBlank { null }
                            )
                        )

                        status = result.fold(
                            onSuccess = {
                                refreshCredits()
                                "Repayment saved and outstanding balance updated."
                            },
                            onFailure = { "Repayment failed: ${it.message.orEmpty()}" }
                        )
                    }
                }, modifier = Modifier.fillMaxWidth())
            }
        }

        StatusBanner(status)
    }
}
