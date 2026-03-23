package com.shopkeeper.mobile.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Checkbox
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.ScreenColumn
import com.shopkeeper.mobile.ui.components.ScreenHeader
import com.shopkeeper.mobile.ui.components.SectionTitle
import com.shopkeeper.mobile.ui.components.SoftButton
import com.shopkeeper.mobile.ui.components.StatusBanner
import com.shopkeeper.mobile.ui.test.ShopkeeperTestTags
import kotlinx.coroutines.launch

@Composable
fun AuthScreen(onAuthenticated: () -> Unit = {}) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val gateway = remember(context) { ShopkeeperDataGateway.get(context) }

    var mode by rememberSaveable { mutableStateOf(AuthMode.SignIn) }
    var status by rememberSaveable { mutableStateOf("") }
    var loading by rememberSaveable { mutableStateOf(false) }

    var loginValue by rememberSaveable { mutableStateOf("") }
    var password by rememberSaveable { mutableStateOf("") }

    var fullName by rememberSaveable { mutableStateOf("") }
    var ownerEmail by rememberSaveable { mutableStateOf("") }
    var ownerPassword by rememberSaveable { mutableStateOf("") }
    var shopName by rememberSaveable { mutableStateOf("") }
    var vatEnabled by rememberSaveable { mutableStateOf(true) }
    var vatRatePercent by rememberSaveable { mutableStateOf("7.5") }

    ScreenColumn {
        ScreenHeader(
            title = "Welcome",
            subtitle = "Sign in to an existing account or create a new owner account before using the app."
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            SoftButton(
                text = "Sign In",
                onClick = { mode = AuthMode.SignIn },
                modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.AUTH_MODE_SIGN_IN)
            )
            SoftButton(
                text = "Create Shop",
                onClick = { mode = AuthMode.RegisterOwner },
                modifier = Modifier.weight(1f).testTag(ShopkeeperTestTags.AUTH_MODE_REGISTER)
            )
        }

        when (mode) {
            AuthMode.SignIn -> {
                SectionTitle(
                    title = "Sign in",
                    subtitle = "Use the email and password for an existing owner, manager, or salesperson account."
                )
                OutlinedTextField(
                    value = loginValue,
                    onValueChange = { loginValue = it },
                    label = { Text("Email") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_LOGIN_EMAIL)
                )
                OutlinedTextField(
                    value = password,
                    onValueChange = { password = it },
                    label = { Text("Password") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_LOGIN_PASSWORD)
                )
                BrickButton(
                    text = if (loading) "Signing In..." else "Sign In",
                    onClick = {
                        if (loading) return@BrickButton
                        if (loginValue.isBlank() || password.isBlank()) {
                            status = "Email and password are required."
                            return@BrickButton
                        }
                        scope.launch {
                            loading = true
                            gateway.login(loginValue, password)
                                .onSuccess {
                                    status = ""
                                    onAuthenticated()
                                }
                                .onFailure {
                                    status = "Sign-in failed: ${it.message.orEmpty()}"
                                }
                            loading = false
                        }
                    },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_LOGIN_SUBMIT)
                )
            }

            AuthMode.RegisterOwner -> {
                SectionTitle(
                    title = "Create owner account",
                    subtitle = "Create the first shop owner account for a new business."
                )
                OutlinedTextField(
                    value = fullName,
                    onValueChange = { fullName = it },
                    label = { Text("Full Name") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_FULL_NAME)
                )
                OutlinedTextField(
                    value = ownerEmail,
                    onValueChange = { ownerEmail = it },
                    label = { Text("Email") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_EMAIL)
                )
                OutlinedTextField(
                    value = ownerPassword,
                    onValueChange = { ownerPassword = it },
                    label = { Text("Password") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_PASSWORD)
                )
                OutlinedTextField(
                    value = shopName,
                    onValueChange = { shopName = it },
                    label = { Text("Shop Name") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_SHOP_NAME)
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Checkbox(checked = vatEnabled, onCheckedChange = { vatEnabled = it })
                    Text("Enable VAT for this shop", color = MaterialTheme.colorScheme.onBackground)
                }
                OutlinedTextField(
                    value = vatRatePercent,
                    onValueChange = { vatRatePercent = it },
                    label = { Text("VAT Rate (%)") },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_VAT_RATE)
                )
                BrickButton(
                    text = if (loading) "Creating Account..." else "Create Owner Account",
                    onClick = {
                        if (loading) return@BrickButton
                        if (fullName.isBlank() || ownerEmail.isBlank() || ownerPassword.isBlank() || shopName.isBlank()) {
                            status = "Full name, email, password, and shop name are required."
                            return@BrickButton
                        }
                        val vatRate = vatRatePercent.toDoubleOrNull()?.div(100.0)
                        if (vatEnabled && (vatRate == null || vatRate < 0.0)) {
                            status = "Enter a valid VAT rate percentage."
                            return@BrickButton
                        }
                        scope.launch {
                            loading = true
                            gateway.registerOwner(
                                fullName = fullName,
                                email = ownerEmail,
                                password = ownerPassword,
                                shopName = shopName,
                                vatEnabled = vatEnabled,
                                vatRate = vatRate ?: 0.0
                            ).onSuccess {
                                status = ""
                                onAuthenticated()
                            }.onFailure {
                                status = "Owner registration failed: ${it.message.orEmpty()}"
                            }
                            loading = false
                        }
                    },
                    modifier = Modifier.fillMaxWidth().testTag(ShopkeeperTestTags.AUTH_REGISTER_SUBMIT)
                )
            }
        }

        if (status.isNotBlank()) {
            StatusBanner(status)
        }
    }
}

private enum class AuthMode {
    SignIn,
    RegisterOwner
}
