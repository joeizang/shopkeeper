import SwiftUI

private enum AuthMode: String, CaseIterable, Identifiable {
    case login = "Sign In"
    case register = "Create Shop"
    var id: String { rawValue }
}

struct LoginView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var mode: AuthMode = .login
    @State private var email = ""
    @State private var password = ""
    @State private var fullName = ""
    @State private var shopName = ""
    @State private var vatEnabled = true
    @State private var vatRatePercent = "7.5"
    @State private var isSubmitting = false

    var body: some View {
        ScreenColumn {
            Spacer()
                .frame(height: SKSpacing.xxl)

            // Brand header
            VStack(spacing: SKSpacing.sm) {
                Image(systemName: "storefront")
                    .font(.system(size: 48))
                    .foregroundStyle(Color.skPrimary)

                Text("Shopkeeper")
                    .font(.skHeadlineLarge)
                    .foregroundStyle(Color.skOnBackground)

                Text("Manage your shop, anywhere")
                    .font(.skBodyMedium)
                    .foregroundStyle(Color.skOnSurfaceVariant)
            }
            .frame(maxWidth: .infinity)
            .padding(.bottom, SKSpacing.xl)

            // Mode toggle
            HStack(spacing: SKSpacing.sm) {
                ForEach(AuthMode.allCases) { authMode in
                    SelectionPill(
                        title: authMode.rawValue,
                        isSelected: mode == authMode,
                        accessibilityId: authMode == .login ? "auth.mode.signIn" : "auth.mode.register"
                    ) {
                        mode = authMode
                    }
                }
            }
            .padding(.bottom, SKSpacing.lg)

            // Registration fields
            if mode == .register {
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Your Details")
                        SKTextField(label: "Full Name", text: $fullName, accessibilityId: "auth.register.fullName")
                        SKTextField(label: "Shop Name", text: $shopName, accessibilityId: "auth.register.shopName")
                        HStack {
                            Toggle(isOn: $vatEnabled) {
                                Text("VAT Enabled")
                                    .font(.skBodyMedium)
                                    .foregroundStyle(Color.skOnSurface)
                            }
                            .tint(.skPrimary)
                        }
                        if vatEnabled {
                            SKTextField(label: "VAT Rate (%)", text: $vatRatePercent, isDecimal: true, accessibilityId: "auth.register.vatRate")
                        }
                    }
                }
            }

            // Credentials
            AccentCard {
                VStack(spacing: SKSpacing.md) {
                    SectionTitle(title: "Credentials")
                    SKTextField(label: "Email", text: $email, isEmail: true, accessibilityId: mode == .login ? "auth.login.email" : "auth.register.email")
                    SKSecureField(label: "Password", text: $password, accessibilityId: mode == .login ? "auth.login.password" : "auth.register.password")
                }
            }

            // Submit
            BrickButton(
                title: mode == .login ? "Sign In" : "Create Shop",
                action: submit,
                isLoading: isSubmitting,
                isDisabled: email.isEmpty || password.isEmpty,
                accessibilityId: mode == .login ? "auth.login.submit" : "auth.register.submit"
            )
            .padding(.top, SKSpacing.sm)
        }
        .overlay(alignment: .bottom) {
            if let status = sessionStore.statusMessage, !status.isEmpty {
                StatusBanner(message: status, kind: .error)
                    .padding(SKSpacing.lg)
            }
        }
    }

    private func submit() {
        guard !isSubmitting else { return }
        isSubmitting = true
        Task {
            if mode == .login {
                await sessionStore.login(login: email, password: password)
            } else {
                await sessionStore.registerOwner(
                    fullName: fullName,
                    email: email,
                    password: password,
                    shopName: shopName,
                    vatEnabled: vatEnabled,
                    vatRate: (Double(vatRatePercent) ?? 7.5) / 100.0
                )
            }
            isSubmitting = false
        }
    }
}
