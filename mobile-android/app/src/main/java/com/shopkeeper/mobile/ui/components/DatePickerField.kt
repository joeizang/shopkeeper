package com.shopkeeper.mobile.ui.components

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.DateRange
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DatePickerField(
    label: String,
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier
) {
    var showPicker by remember { mutableStateOf(false) }
    val parsedDate = remember(value) { parseLocalDate(value) }
    val pickerState = rememberDatePickerState(
        initialSelectedDateMillis = parsedDate?.atStartOfDay(ZoneOffset.UTC)?.toInstant()?.toEpochMilli()
    )

    OutlinedTextField(
        value = parsedDate?.toString().orEmpty(),
        onValueChange = {},
        readOnly = true,
        label = { Text(label) },
        trailingIcon = {
            IconButton(onClick = { showPicker = true }) {
                Icon(
                    imageVector = Icons.Outlined.DateRange,
                    contentDescription = "Pick date",
                    tint = MaterialTheme.colorScheme.secondary
                )
            }
        },
        modifier = modifier
    )

    if (showPicker) {
        DatePickerDialog(
            onDismissRequest = { showPicker = false },
            confirmButton = {
                TextButton(onClick = {
                    val picked = pickerState.selectedDateMillis
                    if (picked != null) {
                        val localDate = Instant.ofEpochMilli(picked).atZone(ZoneOffset.UTC).toLocalDate()
                        onValueChange(localDate.toString())
                    }
                    showPicker = false
                }) {
                    Text("OK")
                }
            },
            dismissButton = {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TextButton(onClick = {
                        onValueChange("")
                        showPicker = false
                    }) {
                        Text("Clear")
                    }
                    TextButton(onClick = { showPicker = false }) {
                        Text("Cancel")
                    }
                }
            }
        ) {
            DatePicker(state = pickerState)
        }
    }
}

private fun parseLocalDate(value: String): LocalDate? {
    if (value.isBlank()) return null
    return runCatching { LocalDate.parse(value.take(10)) }.getOrNull()
}
