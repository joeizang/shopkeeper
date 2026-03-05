package com.shopkeeper.mobile.sync

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.core.data.local.SyncConflictEntity
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import kotlinx.coroutines.launch

@Composable
fun ConflictResolutionScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var conflicts by remember { mutableStateOf<List<SyncConflictEntity>>(emptyList()) }
    var status by remember { mutableStateOf("") }

    fun refresh() {
        scope.launch {
            conflicts = gateway.getConflicts()
        }
    }

    LaunchedEffect(Unit) {
        refresh()
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text("Sync Conflicts", style = MaterialTheme.typography.titleLarge)

        BrickButton(text = "Refresh", onClick = { refresh() }, modifier = Modifier.fillMaxWidth())

        Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            conflicts.take(25).forEach { conflict ->
                AccentCard(modifier = Modifier.fillMaxWidth()) {
                    Column(modifier = Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("${conflict.entityName} / ${conflict.entityId}")
                    Text(conflict.conflictReason, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    if (conflict.localPayloadJson.isNotBlank()) {
                        Text("Local: ${previewPayload(conflict.localPayloadJson)}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    if (conflict.serverPayloadJson.isNotBlank()) {
                        Text("Server: ${previewPayload(conflict.serverPayloadJson)}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        BrickButton(text = "Keep Server", onClick = {
                            scope.launch {
                                gateway.resolveConflictKeepServer(conflict.id)
                                refresh()
                                status = "Resolved with server version"
                            }
                        }, modifier = Modifier.weight(1f))
                        BrickButton(text = "Keep Local", onClick = {
                            scope.launch {
                                gateway.resolveConflictKeepLocal(conflict.id)
                                gateway.runSyncOnce()
                                refresh()
                                status = "Requeued local change"
                            }
                        }, modifier = Modifier.weight(1f))
                    }
                    }
                    }
            }
        }

        if (status.isNotBlank()) {
            Text(status, color = MaterialTheme.colorScheme.secondary)
        }
    }
}

private fun previewPayload(payload: String): String {
    val compact = payload.replace('\n', ' ').replace(Regex("\\s+"), " ").trim()
    return if (compact.length <= 180) compact else "${compact.take(180)}..."
}
