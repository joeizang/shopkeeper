package com.shopkeeper.mobile.profile

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.AccountProfileUpdateInput
import com.shopkeeper.mobile.core.data.AccountSession
import com.shopkeeper.mobile.core.data.LinkedIdentity
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import kotlinx.coroutines.launch

@Composable
fun ProfileScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var fullName by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var phone by remember { mutableStateOf("") }
    var avatarUrl by remember { mutableStateOf("") }
    var preferredLanguage by remember { mutableStateOf("en") }
    var timezone by remember { mutableStateOf("Africa/Lagos") }
    var status by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }

    val sessions = remember { mutableStateListOf<AccountSession>() }
    val identities = remember { mutableStateListOf<LinkedIdentity>() }

    var magicEmail by remember { mutableStateOf("") }
    var magicToken by remember { mutableStateOf("") }
    var debugMagicToken by remember { mutableStateOf("") }
    var googleIdToken by remember { mutableStateOf("") }

    fun loadProfileData() {
        scope.launch {
            loading = true
            runCatching {
                val profile = gateway.getAccountProfile().getOrThrow()
                val linked = gateway.getLinkedIdentities().getOrThrow()
                val activeSessions = gateway.getAccountSessions().getOrThrow()
                Triple(profile, linked, activeSessions)
            }.onSuccess { result ->
                val profile = result.first
                fullName = profile.fullName
                email = profile.email.orEmpty()
                phone = profile.phone.orEmpty()
                avatarUrl = profile.avatarUrl.orEmpty()
                preferredLanguage = profile.preferredLanguage
                timezone = profile.timezone
                magicEmail = profile.email.orEmpty()
                identities.clear()
                identities.addAll(result.second)
                sessions.clear()
                sessions.addAll(result.third)
                status = ""
            }.onFailure {
                status = "Could not load account details: ${it.message.orEmpty()}"
            }
            loading = false
        }
    }

    LaunchedEffect(Unit) {
        loadProfileData()
    }

    ScreenColumn {
        ScreenHeader(
            title = "Account",
            subtitle = "Manage profile details, linked sign-in methods, and active sessions."
        )

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                SectionTitle("Profile details")
                OutlinedTextField(
                    value = fullName,
                    onValueChange = { fullName = it },
                    label = { Text("Full Name") },
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = email,
                    onValueChange = {},
                    readOnly = true,
                    label = { Text("Email") },
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = phone,
                    onValueChange = { phone = it },
                    label = { Text("Phone") },
                    modifier = Modifier.fillMaxWidth()
                )
                OutlinedTextField(
                    value = avatarUrl,
                    onValueChange = { avatarUrl = it },
                    label = { Text("Avatar URL") },
                    modifier = Modifier.fillMaxWidth()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    OutlinedTextField(
                        value = preferredLanguage,
                        onValueChange = { preferredLanguage = it },
                        label = { Text("Language") },
                        modifier = Modifier.weight(1f)
                    )
                    OutlinedTextField(
                        value = timezone,
                        onValueChange = { timezone = it },
                        label = { Text("Timezone") },
                        modifier = Modifier.weight(1f)
                    )
                }
                BrickButton(
                    text = if (loading) "Saving..." else "Save Profile",
                    onClick = {
                        if (loading) return@BrickButton
                        scope.launch {
                            loading = true
                            val result = gateway.updateAccountProfile(
                                AccountProfileUpdateInput(
                                    fullName = fullName,
                                    phone = phone.ifBlank { null },
                                    avatarUrl = avatarUrl.ifBlank { null },
                                    preferredLanguage = preferredLanguage.ifBlank { null },
                                    timezone = timezone.ifBlank { null }
                                )
                            )
                            loading = false
                            result.onSuccess {
                                status = "Profile updated."
                                loadProfileData()
                            }.onFailure {
                                status = "Profile update failed: ${it.message.orEmpty()}"
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SectionTitle("Linked sign-in methods")
                if (identities.isEmpty()) {
                    Text("No linked identities yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    identities.forEach { identity ->
                        Text(
                            "${identity.provider.uppercase()} • ${identity.email ?: identity.providerSubject}",
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            }
        }

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SectionTitle(
                    title = "Magic link",
                    subtitle = "Request a sign-in link and verify it manually during development."
                )
                OutlinedTextField(
                    value = magicEmail,
                    onValueChange = { magicEmail = it },
                    label = { Text("Email for Magic Link") },
                    modifier = Modifier.fillMaxWidth()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    BrickButton(
                        text = "Request Link",
                        onClick = {
                            scope.launch {
                                gateway.requestMagicLink(magicEmail)
                                    .onSuccess {
                                        debugMagicToken = it.debugToken.orEmpty()
                                        status = it.message
                                    }
                                    .onFailure {
                                        status = "Magic-link request failed: ${it.message.orEmpty()}"
                                    }
                            }
                        },
                        modifier = Modifier.weight(1f)
                    )
                    SoftButton(
                        text = "Refresh",
                        onClick = { loadProfileData() },
                        modifier = Modifier.weight(1f)
                    )
                }
                OutlinedTextField(
                    value = magicToken,
                    onValueChange = { magicToken = it },
                    label = { Text("Magic Link Token") },
                    modifier = Modifier.fillMaxWidth()
                )
                if (debugMagicToken.isNotBlank()) {
                    Text(
                        "Debug token: $debugMagicToken",
                        color = MaterialTheme.colorScheme.secondary,
                        style = MaterialTheme.typography.labelSmall
                    )
                }
                BrickButton(
                    text = "Verify Magic Link",
                    onClick = {
                        scope.launch {
                            gateway.verifyMagicLink(if (magicToken.isBlank()) debugMagicToken else magicToken)
                                .onSuccess {
                                    status = "Magic-link verification successful."
                                    loadProfileData()
                                }
                                .onFailure {
                                    status = "Magic-link verification failed: ${it.message.orEmpty()}"
                                }
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SectionTitle(
                    title = "Google sign-in",
                    subtitle = "Paste an ID token to test the mobile auth endpoint."
                )
                OutlinedTextField(
                    value = googleIdToken,
                    onValueChange = { googleIdToken = it },
                    label = { Text("Google ID Token") },
                    modifier = Modifier.fillMaxWidth()
                )
                BrickButton(
                    text = "Verify Google Token",
                    onClick = {
                        scope.launch {
                            gateway.loginWithGoogleIdToken(googleIdToken)
                                .onSuccess {
                                    status = "Google token verified and session updated."
                                    loadProfileData()
                                }
                                .onFailure {
                                    status = "Google login failed: ${it.message.orEmpty()}"
                                }
                        }
                    },
                    modifier = Modifier.fillMaxWidth()
                )
            }
        }

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Column(
                modifier = Modifier.padding(16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                SectionTitle("Active sessions")
                if (sessions.isEmpty()) {
                    Text("No sessions recorded.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    sessions.forEach { session ->
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Column(modifier = Modifier.weight(1f)) {
                                Text(
                                    "${session.role} • ${session.deviceName ?: "Unknown device"}",
                                    color = MaterialTheme.colorScheme.onBackground
                                )
                                Text(
                                    "Expires: ${session.expiresAtUtc.take(19)}",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    style = MaterialTheme.typography.labelSmall
                                )
                            }
                            if (!session.isRevoked) {
                                SoftButton(
                                    text = "Revoke",
                                    onClick = {
                                        scope.launch {
                                            gateway.revokeAccountSession(session.sessionId)
                                                .onSuccess {
                                                    status = "Session revoked."
                                                    loadProfileData()
                                                }
                                                .onFailure {
                                                    status = "Session revoke failed: ${it.message.orEmpty()}"
                                                }
                                        }
                                    }
                                )
                            }
                        }
                    }
                }
            }
        }

        StatusBanner(status)
    }
}
