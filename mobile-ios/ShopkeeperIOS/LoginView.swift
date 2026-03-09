import SwiftUI

private enum AuthMode: String, CaseIterable, Identifiable {
    case login = "Sign In"
    case register = "Create Owner"
    var id: String { rawValue }
}

struct LoginView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var mode: AuthMode = .login
    @State private var email = "owner@shopkeeper.local"
    @State private var password = "Shopkeeper123!"
    @State private var fullName = "Shop Owner"
    @State private var shopName = "Shopkeeper iPhone"
    @State private var vatEnabled = true
    @State private var vatRatePercent = "7.5"
    @State private var isSubmitting = false

    var body: some View {
        Form {
            Picker("Mode", selection: $mode) {
                ForEach(AuthMode.allCases) { mode in
                    Text(mode.rawValue).tag(mode)
                }
            }
            .pickerStyle(.segmented)

            if mode == .register {
                TextField("Full Name", text: $fullName)
                TextField("Shop Name", text: $shopName)
                Toggle("VAT Enabled", isOn: $vatEnabled)
                TextField("VAT Rate (%)", text: $vatRatePercent)
                    .keyboardType(.decimalPad)
            }

            TextField("Email", text: $email)
                .keyboardType(.emailAddress)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
            SecureField("Password", text: $password)

            Button(isSubmitting ? "Working..." : (mode == .login ? "Sign In" : "Create Owner")) {
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
            .buttonStyle(.borderedProminent)
        }
        .navigationTitle("Shopkeeper iPhone")
        .overlay(alignment: .bottom) {
            if let status = sessionStore.statusMessage, !status.isEmpty {
                Text(status)
                    .font(.footnote)
                    .foregroundStyle(.white)
                    .padding(12)
                    .frame(maxWidth: .infinity)
                    .background(Color.red.opacity(0.82))
                    .clipShape(RoundedRectangle(cornerRadius: 14))
                    .padding()
            }
        }
    }
}
