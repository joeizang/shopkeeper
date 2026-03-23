import SwiftUI

struct OnboardingPage {
    let icon: String
    let title: String
    let subtitle: String
}

private let onboardingPages = [
    OnboardingPage(
        icon: "rectangle.grid.2x2",
        title: "Track Everything",
        subtitle: "Monitor revenue, inventory, and sales in real time from your dashboard."
    ),
    OnboardingPage(
        icon: "cart",
        title: "Sell Smarter",
        subtitle: "Create sales with OCR scanning, split payments, and automatic VAT calculation."
    ),
    OnboardingPage(
        icon: "arrow.triangle.2.circlepath",
        title: "Stay in Sync",
        subtitle: "Your data syncs across devices. Resolve conflicts and manage your team from anywhere."
    )
]

struct OnboardingView: View {
    let onComplete: () -> Void
    @State private var currentPage = 0

    var body: some View {
        ZStack {
            ShopkeeperBackground()

            VStack(spacing: 0) {
                // Skip button
                HStack {
                    Spacer()
                    Button("Skip") {
                        onComplete()
                    }
                    .font(.skLabelLarge)
                    .foregroundStyle(Color.skOnSurfaceVariant)
                }
                .padding(.horizontal, SKSpacing.lg)
                .padding(.top, SKSpacing.lg)

                Spacer()

                // Page content
                TabView(selection: $currentPage) {
                    ForEach(Array(onboardingPages.enumerated()), id: \.offset) { index, page in
                        VStack(spacing: 0) {
                            Image(systemName: page.icon)
                                .font(.system(size: 56, weight: .light))
                                .foregroundStyle(Color.skPrimary)

                            Spacer().frame(height: SKSpacing.xxl)

                            Text(page.title)
                                .font(.skHeadlineLarge)
                                .foregroundStyle(Color.skOnBackground)
                                .multilineTextAlignment(.center)

                            Spacer().frame(height: SKSpacing.md)

                            Text(page.subtitle)
                                .font(.skBodyLarge)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                                .multilineTextAlignment(.center)
                                .padding(.horizontal, SKSpacing.lg)
                        }
                        .tag(index)
                    }
                }
                .tabViewStyle(.page(indexDisplayMode: .never))

                Spacer()

                // Dot indicators
                HStack(spacing: SKSpacing.sm) {
                    ForEach(0..<onboardingPages.count, id: \.self) { index in
                        Circle()
                            .fill(index == currentPage ? Color.skPrimary : Color.skOnSurfaceVariant.opacity(0.3))
                            .frame(width: index == currentPage ? 10 : 8, height: index == currentPage ? 10 : 8)
                            .animation(.easeInOut(duration: 0.2), value: currentPage)
                    }
                }
                .padding(.bottom, SKSpacing.xxl)

                // Next / Get Started button
                BrickButton(
                    title: currentPage == onboardingPages.count - 1 ? "Get Started" : "Next"
                ) {
                    if currentPage == onboardingPages.count - 1 {
                        onComplete()
                    } else {
                        withAnimation {
                            currentPage += 1
                        }
                    }
                }
                .padding(.horizontal, SKSpacing.lg)
                .padding(.bottom, SKSpacing.xl)
            }
        }
    }
}
