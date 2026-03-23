package com.shopkeeper.mobile.onboarding

import android.content.Context
import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Dashboard
import androidx.compose.material.icons.outlined.ShoppingCart
import androidx.compose.material.icons.outlined.Sync
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import com.shopkeeper.mobile.ui.components.BrickButton
import com.shopkeeper.mobile.ui.components.ShopkeeperBackground

private data class OnboardingPage(
    val icon: ImageVector,
    val title: String,
    val subtitle: String
)

private val pages = listOf(
    OnboardingPage(
        icon = Icons.Outlined.Dashboard,
        title = "Track Everything",
        subtitle = "Monitor revenue, inventory, and sales in real time from your dashboard."
    ),
    OnboardingPage(
        icon = Icons.Outlined.ShoppingCart,
        title = "Sell Smarter",
        subtitle = "Create sales with OCR scanning, split payments, and automatic VAT calculation."
    ),
    OnboardingPage(
        icon = Icons.Outlined.Sync,
        title = "Stay in Sync",
        subtitle = "Your data syncs across devices. Resolve conflicts and manage your team from anywhere."
    )
)

@Composable
fun OnboardingScreen(onComplete: () -> Unit) {
    val context = LocalContext.current
    var currentPage by remember { mutableIntStateOf(0) }

    fun completeOnboarding() {
        context.getSharedPreferences("shopkeeper_prefs", Context.MODE_PRIVATE)
            .edit()
            .putBoolean("onboarding_completed", true)
            .apply()
        onComplete()
    }

    ShopkeeperBackground(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 24.dp, vertical = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Skip button row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.End
            ) {
                TextButton(onClick = { completeOnboarding() }) {
                    Text(
                        text = "Skip",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        style = MaterialTheme.typography.labelLarge
                    )
                }
            }

            Spacer(modifier = Modifier.weight(1f))

            // Animated page content
            AnimatedContent(
                targetState = currentPage,
                transitionSpec = {
                    val direction = if (targetState > initialState) 1 else -1
                    (slideInHorizontally { fullWidth -> direction * fullWidth } + fadeIn())
                        .togetherWith(slideOutHorizontally { fullWidth -> -direction * fullWidth } + fadeOut())
                },
                label = "onboarding_page"
            ) { page ->
                val data = pages[page]
                Column(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalAlignment = Alignment.CenterHorizontally
                ) {
                    Icon(
                        imageVector = data.icon,
                        contentDescription = data.title,
                        modifier = Modifier.size(56.dp),
                        tint = MaterialTheme.colorScheme.primary
                    )
                    Spacer(modifier = Modifier.height(32.dp))
                    Text(
                        text = data.title,
                        style = MaterialTheme.typography.headlineLarge,
                        color = MaterialTheme.colorScheme.onBackground,
                        textAlign = TextAlign.Center
                    )
                    Spacer(modifier = Modifier.height(12.dp))
                    Text(
                        text = data.subtitle,
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.padding(horizontal = 16.dp)
                    )
                }
            }

            Spacer(modifier = Modifier.weight(1f))

            // Dot indicators
            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.padding(bottom = 32.dp)
            ) {
                pages.forEachIndexed { index, _ ->
                    Box(
                        modifier = Modifier
                            .size(if (index == currentPage) 10.dp else 8.dp)
                            .clip(CircleShape)
                            .background(
                                if (index == currentPage)
                                    MaterialTheme.colorScheme.primary
                                else
                                    MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.3f)
                            )
                    )
                }
            }

            // Next / Get Started button
            BrickButton(
                text = if (currentPage == pages.lastIndex) "Get Started" else "Next",
                onClick = {
                    if (currentPage == pages.lastIndex) {
                        completeOnboarding()
                    } else {
                        currentPage++
                    }
                },
                modifier = Modifier.fillMaxWidth()
            )

            Spacer(modifier = Modifier.height(24.dp))
        }
    }
}
