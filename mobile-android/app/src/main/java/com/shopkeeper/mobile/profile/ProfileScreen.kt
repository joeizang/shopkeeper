package com.shopkeeper.mobile.profile

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.AccountProfileUpdateInput
import com.shopkeeper.mobile.core.data.AccountSession
import com.shopkeeper.mobile.core.data.LinkedIdentity
import com.shopkeeper.mobile.core.data.ShopRole
import com.shopkeeper.mobile.core.data.ShopSummary
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.core.data.StaffMemberRecord
import com.shopkeeper.mobile.ui.components.AccentCard
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.MetricCard
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SelectionPill
import com.shopkeeper.mobile.ui.components.StaffRoleDropdown
import com.shopkeeper.mobile.ui.components.StaffRoleOption
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

@Composable
fun ProfileScreen() {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }
    val capabilities = gateway.sessionCapabilities()
    val isOwner = capabilities.canManageStaff

    var fullName by remember { mutableStateOf("") }
    var email by remember { mutableStateOf("") }
    var phone by remember { mutableStateOf("") }
    var avatarUrl by remember { mutableStateOf("") }
    var preferredLanguage by remember { mutableStateOf("en") }
    var timezone by remember { mutableStateOf("Africa/Lagos") }
    var status by remember { mutableStateOf("") }
    var loading by remember { mutableStateOf(false) }
    var currentShop by remember { mutableStateOf<ShopSummary?>(null) }
    var shopVatEnabled by remember { mutableStateOf(true) }
    var shopVatRatePercent by remember { mutableStateOf("7.5") }
    var shopDiscountPercent by remember { mutableStateOf("0") }

    val sessions = remember { mutableStateListOf<AccountSession>() }
    val identities = remember { mutableStateListOf<LinkedIdentity>() }
    val staffMembers = remember { mutableStateListOf<StaffMemberRecord>() }

    var magicEmail by remember { mutableStateOf("") }
    var magicToken by remember { mutableStateOf("") }
    var developmentMagicToken by remember { mutableStateOf("") }
    var googleIdToken by remember { mutableStateOf("") }
    var inviteFullName by remember { mutableStateOf("") }
    var inviteEmail by remember { mutableStateOf("") }
    var invitePhone by remember { mutableStateOf("") }
    var invitePassword by remember { mutableStateOf("Shopkeeper123!") }
    var inviteRole by remember { mutableStateOf(StaffRoleOption.Salesperson) }

    fun loadProfileData() {
        scope.launch {
            loading = true
            runCatching {
                val profile = gateway.getAccountProfile().getOrThrow()
                val linked = gateway.getLinkedIdentities().getOrThrow()
                val activeSessions = gateway.getAccountSessions().getOrThrow()
                val shop = gateway.getCurrentShop().getOrNull()
                val team = if (isOwner && shop != null) gateway.getShopStaff(shop.id).getOrDefault(emptyList()) else emptyList()
                ProfileBundle(profile, linked, activeSessions, shop, team)
            }.onSuccess { result ->
                val profile = result.profile
                fullName = profile.fullName
                email = profile.email.orEmpty()
                phone = profile.phone.orEmpty()
                avatarUrl = profile.avatarUrl.orEmpty()
                preferredLanguage = profile.preferredLanguage
                timezone = profile.timezone
                magicEmail = profile.email.orEmpty()
                currentShop = result.shop
                shopVatEnabled = result.shop?.vatEnabled ?: true
                shopVatRatePercent = (((result.shop?.vatRate ?: 0.075) * 100.0).toString()).trimEnd('0').trimEnd('.')
                shopDiscountPercent = (((result.shop?.defaultDiscountPercent ?: 0.0) * 100.0).toString()).trimEnd('0').trimEnd('.')
                identities.clear()
                identities.addAll(result.identities)
                sessions.clear()
                sessions.addAll(result.sessions)
                staffMembers.clear()
                staffMembers.addAll(result.staffMembers)
                status = ""
            }.onFailure {
                status = "Could not load account details: ${it.message.orEmpty()}"
            }
            loading = false
        }
    }

    fun inviteStaffMember() {
        val shop = currentShop ?: run {
            status = "No active shop selected."
            return
        }
        if (inviteFullName.isBlank()) {
            status = "Staff full name is required."
            return
        }
        if (inviteEmail.isBlank() && invitePhone.isBlank()) {
            status = "Provide email or phone for the staff member."
            return
        }

        scope.launch {
            loading = true
            gateway.inviteShopStaff(
                shopId = shop.id,
                fullName = inviteFullName,
                email = inviteEmail.ifBlank { null },
                phone = invitePhone.ifBlank { null },
                temporaryPassword = invitePassword,
                role = when (inviteRole) {
                    StaffRoleOption.ShopManager -> ShopRole.ShopManager
                    StaffRoleOption.Salesperson -> ShopRole.Salesperson
                }
            ).onSuccess {
                status = "Staff invite created."
                inviteFullName = ""
                inviteEmail = ""
                invitePhone = ""
                invitePassword = "Shopkeeper123!"
                inviteRole = StaffRoleOption.Salesperson
                loadProfileData()
            }.onFailure {
                status = "Staff invite failed: ${it.message.orEmpty()}"
            }
            loading = false
        }
    }

    fun updateStaffMember(member: StaffMemberRecord, role: ShopRole = member.role, isActive: Boolean = member.isActive) {
        val shop = currentShop ?: return
        scope.launch {
            loading = true
            gateway.updateShopStaff(shop.id, member.staffId, role, isActive)
                .onSuccess {
                    status = "Staff member updated."
                    loadProfileData()
                }
                .onFailure {
                    status = "Staff update failed: ${it.message.orEmpty()}"
                }
            loading = false
        }
    }

    fun revokeAllSessions() {
        scope.launch {
            loading = true
            gateway.revokeAllAccountSessions()
                .onSuccess {
                    status = "All active sessions revoked."
                    loadProfileData()
                }
                .onFailure {
                    status = "Bulk revoke failed: ${it.message.orEmpty()}"
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
            subtitle = "Manage your profile, sign-in methods, and active devices."
        )

        AccentCard(modifier = Modifier.fillMaxWidth()) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(16.dp),
                horizontalArrangement = Arrangement.spacedBy(14.dp),
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(
                    modifier = Modifier
                        .size(56.dp)
                        .clip(CircleShape)
                        .background(MaterialTheme.colorScheme.primary),
                    horizontalArrangement = Arrangement.Center,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        initialsOf(fullName),
                        color = MaterialTheme.colorScheme.onPrimary,
                        fontWeight = FontWeight.Bold,
                        style = MaterialTheme.typography.titleMedium
                    )
                }
                Column(
                    modifier = Modifier.weight(1f),
                    verticalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    Text(fullName.ifBlank { "Shopkeeper User" }, style = MaterialTheme.typography.titleLarge)
                    Text(email.ifBlank { "No email on file" }, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    currentShop?.let { shop ->
                        Text(
                            "${shop.name} • ${shop.role}",
                            color = MaterialTheme.colorScheme.secondary,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                }
            }
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            MetricCard(
                title = "Linked Methods",
                value = identities.size.toString(),
                supporting = if (identities.any { it.emailVerified }) "Verified access present" else "Review access methods",
                modifier = Modifier.weight(1f)
            )
            MetricCard(
                title = "Active Sessions",
                value = sessions.count { !it.isRevoked }.toString(),
                supporting = "${sessions.size} total recorded",
                modifier = Modifier.weight(1f)
            )
        }

        currentShop?.let { shop ->
            AccentCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    SectionTitle("Current shop")
                    Text(shop.name, style = MaterialTheme.typography.titleMedium)
                    Text(
                        "Code: ${shop.code} • Role: ${shop.role}",
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        "VAT: ${if (shop.vatEnabled) "${(shop.vatRate * 100).toInt()}%" else "Disabled"}",
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Text(
                        "Default discount: ${"%.1f".format(shop.defaultDiscountPercent * 100)}%",
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    if (isOwner) {
                        SectionTitle(
                            title = "Pricing settings",
                            subtitle = "Control tax and the preset discount that sales staff can apply."
                        )
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            SelectionPill(
                                text = "VAT Enabled",
                                selected = shopVatEnabled,
                                onClick = { shopVatEnabled = true },
                                modifier = Modifier.weight(1f)
                            )
                            SelectionPill(
                                text = "VAT Disabled",
                                selected = !shopVatEnabled,
                                onClick = { shopVatEnabled = false },
                                modifier = Modifier.weight(1f)
                            )
                        }
                        OutlinedTextField(
                            value = shopVatRatePercent,
                            onValueChange = { shopVatRatePercent = it },
                            enabled = shopVatEnabled,
                            label = { Text("VAT Rate (%)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                        OutlinedTextField(
                            value = shopDiscountPercent,
                            onValueChange = { shopDiscountPercent = it },
                            label = { Text("Default Discount (%)") },
                            modifier = Modifier.fillMaxWidth()
                        )
                        BrickButton(
                            text = if (loading) "Saving..." else "Save Pricing Settings",
                            onClick = {
                                val percent = shopVatRatePercent.toDoubleOrNull()
                                val discountPercent = shopDiscountPercent.toDoubleOrNull()
                                if (shopVatEnabled && (percent == null || percent < 0.0 || percent > 100.0)) {
                                    status = "Enter a VAT percentage between 0 and 100."
                                    return@BrickButton
                                }
                                if (discountPercent == null || discountPercent < 0.0 || discountPercent > 100.0) {
                                    status = "Enter a discount percentage between 0 and 100."
                                    return@BrickButton
                                }
                                scope.launch {
                                    loading = true
                                    gateway.updateShopVatSettings(
                                        shopId = shop.id,
                                        vatEnabled = shopVatEnabled,
                                        vatRate = if (shopVatEnabled) (percent ?: 0.0) / 100.0 else 0.0,
                                        defaultDiscountPercent = discountPercent / 100.0,
                                        rowVersionBase64 = currentShop?.rowVersionBase64
                                    ).onSuccess { updatedShop ->
                                        currentShop = updatedShop
                                        shopVatEnabled = updatedShop.vatEnabled
                                        shopVatRatePercent = (((updatedShop.vatRate) * 100.0).toString()).trimEnd('0').trimEnd('.')
                                        shopDiscountPercent = (((updatedShop.defaultDiscountPercent) * 100.0).toString()).trimEnd('0').trimEnd('.')
                                        status = "Pricing settings updated."
                                    }.onFailure {
                                        status = "Pricing settings update failed: ${it.message.orEmpty()}"
                                    }
                                    loading = false
                                }
                            },
                            modifier = Modifier.fillMaxWidth()
                        )
                    } else {
                        Text(
                            "Only the shop owner can update pricing settings.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                }
            }
        }

        if (isOwner) {
            AccentCard(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(10.dp)
                ) {
                    SectionTitle(
                        title = "Team management",
                        subtitle = "Invite shop managers or salespeople and control who is active."
                    )
                    OutlinedTextField(
                        value = inviteFullName,
                        onValueChange = { inviteFullName = it },
                        label = { Text("Full Name") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = inviteEmail,
                        onValueChange = { inviteEmail = it },
                        label = { Text("Email") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = invitePhone,
                        onValueChange = { invitePhone = it },
                        label = { Text("Phone") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    OutlinedTextField(
                        value = invitePassword,
                        onValueChange = { invitePassword = it },
                        label = { Text("Temporary Password") },
                        modifier = Modifier.fillMaxWidth()
                    )
                    StaffRoleDropdown(
                        selected = inviteRole,
                        onSelected = { inviteRole = it },
                        modifier = Modifier.fillMaxWidth()
                    )
                    BrickButton(
                        text = if (loading) "Inviting..." else "Invite Staff",
                        onClick = { if (!loading) inviteStaffMember() },
                        modifier = Modifier.fillMaxWidth()
                    )

                    if (staffMembers.isEmpty()) {
                        Text("No staff accounts created yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        staffMembers.forEach { member ->
                            AccentCard(modifier = Modifier.fillMaxWidth()) {
                                Column(
                                    modifier = Modifier.padding(12.dp),
                                    verticalArrangement = Arrangement.spacedBy(8.dp)
                                ) {
                                    Row(
                                        modifier = Modifier.fillMaxWidth(),
                                        horizontalArrangement = Arrangement.SpaceBetween
                                    ) {
                                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                            Text(member.fullName, style = MaterialTheme.typography.titleMedium)
                                            Text(
                                                listOfNotNull(member.email, member.phone).joinToString(" • ").ifBlank { "No contact details" },
                                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                                style = MaterialTheme.typography.bodySmall
                                            )
                                            Text(
                                                "Joined ${formatAccountDateTime(member.createdAtUtc)}",
                                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                                                style = MaterialTheme.typography.bodySmall
                                            )
                                        }
                                        Text(
                                            if (member.isActive) "Active" else "Inactive",
                                            color = if (member.isActive) MaterialTheme.colorScheme.secondary else MaterialTheme.colorScheme.primary,
                                            style = MaterialTheme.typography.labelLarge
                                        )
                                    }
                                    Text(
                                        "Role: ${member.role.displayName}",
                                        color = MaterialTheme.colorScheme.secondary,
                                        style = MaterialTheme.typography.bodySmall
                                    )
                                    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                        SoftButton(
                                            text = if (member.role == ShopRole.ShopManager) "Make Salesperson" else "Make Manager",
                                            onClick = {
                                                updateStaffMember(
                                                    member,
                                                    role = if (member.role == ShopRole.ShopManager) ShopRole.Salesperson else ShopRole.ShopManager,
                                                    isActive = member.isActive
                                                )
                                            },
                                            modifier = Modifier.weight(1f)
                                        )
                                        SoftButton(
                                            text = if (member.isActive) "Disable" else "Activate",
                                            onClick = {
                                                updateStaffMember(
                                                    member,
                                                    role = member.role,
                                                    isActive = !member.isActive
                                                )
                                            },
                                            modifier = Modifier.weight(1f)
                                        )
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

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
                SectionTitle(
                    title = "Linked sign-in methods",
                    subtitle = "Methods already connected to this account."
                )
                if (identities.isEmpty()) {
                    Text("No linked identities yet.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    identities.forEach { identity ->
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween
                        ) {
                            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                Text(identity.provider.replace('_', ' ').uppercase(), style = MaterialTheme.typography.titleMedium)
                                Text(
                                    identity.email ?: identity.providerSubject,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    style = MaterialTheme.typography.bodySmall
                                )
                            }
                            Text(
                                if (identity.emailVerified) "Verified" else "Pending",
                                color = if (identity.emailVerified) MaterialTheme.colorScheme.secondary else MaterialTheme.colorScheme.primary,
                                style = MaterialTheme.typography.labelLarge
                            )
                        }
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
                    title = "Active sessions",
                    subtitle = "Review signed-in devices and revoke any that should no longer have access."
                )
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    SoftButton(
                        text = "Refresh Sessions",
                        onClick = { loadProfileData() },
                        modifier = Modifier.weight(1f)
                    )
                    SoftButton(
                        text = "Revoke All",
                        onClick = { if (!loading) revokeAllSessions() },
                        modifier = Modifier.weight(1f)
                    )
                }
                if (sessions.isEmpty()) {
                    Text("No sessions recorded.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    sessions.forEach { session ->
                        AccentCard(modifier = Modifier.fillMaxWidth()) {
                            Column(
                                modifier = Modifier.padding(12.dp),
                                verticalArrangement = Arrangement.spacedBy(6.dp)
                            ) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.SpaceBetween
                                ) {
                                    Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                                        Text(
                                            session.deviceName ?: "Unknown device",
                                            style = MaterialTheme.typography.titleMedium
                                        )
                                        Text(
                                            "${session.role} • Created ${formatAccountDateTime(session.createdAtUtc)}",
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                        Text(
                                            "Expires ${formatAccountDateTime(session.expiresAtUtc)}",
                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                            style = MaterialTheme.typography.bodySmall
                                        )
                                    }
                                    Text(
                                        if (session.isRevoked) "Revoked" else "Active",
                                        color = if (session.isRevoked) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.secondary,
                                        style = MaterialTheme.typography.labelLarge
                                    )
                                }
                                if (!session.isRevoked) {
                                    SoftButton(
                                        text = "Revoke Session",
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
                                        },
                                        modifier = Modifier.fillMaxWidth()
                                    )
                                }
                            }
                        }
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
                    title = "Magic link sign-in",
                    subtitle = "Delivery plumbing is in place. Use the development token until email sending is enabled."
                )
                OutlinedTextField(
                    value = magicEmail,
                    onValueChange = { magicEmail = it },
                    label = { Text("Email") },
                    modifier = Modifier.fillMaxWidth()
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    BrickButton(
                        text = "Request Link",
                        onClick = {
                            scope.launch {
                                gateway.requestMagicLink(magicEmail)
                                    .onSuccess {
                                        developmentMagicToken = it.debugToken.orEmpty()
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
                        text = "Reload",
                        onClick = { loadProfileData() },
                        modifier = Modifier.weight(1f)
                    )
                }
                OutlinedTextField(
                    value = magicToken,
                    onValueChange = { magicToken = it },
                    label = { Text("Token") },
                    modifier = Modifier.fillMaxWidth()
                )
                if (developmentMagicToken.isNotBlank()) {
                    Text(
                        "Development token: $developmentMagicToken",
                        color = MaterialTheme.colorScheme.secondary,
                        style = MaterialTheme.typography.labelSmall
                    )
                }
                BrickButton(
                    text = "Verify Token",
                    onClick = {
                        scope.launch {
                            gateway.verifyMagicLink(if (magicToken.isBlank()) developmentMagicToken else magicToken)
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
                    subtitle = "Validate a Google ID token against the mobile auth endpoint."
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

        StatusBanner(status)
    }
}

private data class ProfileBundle(
    val profile: com.shopkeeper.mobile.core.data.AccountProfile,
    val identities: List<LinkedIdentity>,
    val sessions: List<AccountSession>,
    val shop: ShopSummary?,
    val staffMembers: List<StaffMemberRecord>
)

private fun initialsOf(name: String): String {
    val parts = name.trim().split(" ").filter { it.isNotBlank() }
    if (parts.isEmpty()) return "SK"
    return parts.take(2).joinToString("") { it.take(1).uppercase() }
}

private fun formatAccountDateTime(value: String): String {
    return runCatching {
        Instant.parse(value).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm"))
    }.getOrDefault(value.take(19))
}
