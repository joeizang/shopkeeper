import SwiftUI
import UIKit

// MARK: - Color Palette (matches Android Theme.kt)

extension Color {
    // Primary brick-red accent
    static let skPrimary = Color(red: 0x9E / 255, green: 0x3D / 255, blue: 0x2D / 255)
    static let skOnPrimary = Color(red: 0xF2 / 255, green: 0xF3 / 255, blue: 0xF5 / 255)

    // Secondary light-gray accent
    static let skSecondary = Color(red: 0xE2 / 255, green: 0xE5 / 255, blue: 0xEA / 255)
    static let skOnSecondary = Color(red: 0x12 / 255, green: 0x14 / 255, blue: 0x18 / 255)

    // Tertiary muted beige
    static let skTertiary = Color(red: 0xB3 / 255, green: 0x92 / 255, blue: 0x8A / 255)

    // Backgrounds
    static let skBackground = Color(red: 0x09 / 255, green: 0x0A / 255, blue: 0x0D / 255)
    static let skBackgroundEnd = Color(red: 0x0D / 255, green: 0x10 / 255, blue: 0x14 / 255)
    static let skOnBackground = Color(red: 0xF4 / 255, green: 0xF6 / 255, blue: 0xFA / 255)

    // Surfaces (card backgrounds)
    static let skSurface = Color(red: 0x13 / 255, green: 0x17 / 255, blue: 0x1D / 255)
    static let skOnSurface = Color(red: 0xF0 / 255, green: 0xF3 / 255, blue: 0xF8 / 255)
    static let skSurfaceVariant = Color(red: 0x1C / 255, green: 0x21 / 255, blue: 0x29 / 255)
    static let skOnSurfaceVariant = Color(red: 0xBB / 255, green: 0xC2 / 255, blue: 0xCC / 255)

    // Borders & outlines
    static let skOutline = Color(red: 0x3A / 255, green: 0x42 / 255, blue: 0x4D / 255)

    // Semantic status colors
    static let skSuccess = Color(red: 0x2D / 255, green: 0x9E / 255, blue: 0x5C / 255)
    static let skWarning = Color(red: 0xD4 / 255, green: 0x9A / 255, blue: 0x2A / 255)
    static let skError = Color(red: 0xCF / 255, green: 0x3E / 255, blue: 0x3E / 255)
}

// MARK: - Typography Scale

extension Font {
    static let skHeadlineLarge = Font.system(size: 28, weight: .bold)
    static let skHeadlineMedium = Font.system(size: 24, weight: .semibold)
    static let skTitleLarge = Font.system(size: 20, weight: .semibold)
    static let skTitleMedium = Font.system(size: 17, weight: .semibold)
    static let skBodyLarge = Font.system(size: 16, weight: .regular)
    static let skBodyMedium = Font.system(size: 14, weight: .regular)
    static let skBodySmall = Font.system(size: 13, weight: .regular)
    static let skLabelLarge = Font.system(size: 14, weight: .semibold)
    static let skLabelSmall = Font.system(size: 12, weight: .medium)
    /// Tabular-lining numerals for financial figures
    static let skMoney = Font.system(size: 17, weight: .semibold).monospacedDigit()
    static let skMoneyLarge = Font.system(size: 28, weight: .bold).monospacedDigit()
}

// MARK: - Spacing Tokens

enum SKSpacing {
    static let xs: CGFloat = 4
    static let sm: CGFloat = 8
    static let md: CGFloat = 12
    static let lg: CGFloat = 16
    static let xl: CGFloat = 24
    static let xxl: CGFloat = 32
}

// MARK: - Shape Tokens

enum SKShape {
    static let small: CGFloat = 8
    static let medium: CGFloat = 10
    static let large: CGFloat = 12
    static let extraLarge: CGFloat = 14
    static let pill: CGFloat = 100
}

// MARK: - Gradient Background

struct ShopkeeperBackground: View {
    var body: some View {
        LinearGradient(
            colors: [.skBackground, .skBackgroundEnd],
            startPoint: .top,
            endPoint: .bottom
        )
        .ignoresSafeArea()
    }
}

// MARK: - Accent Card

struct AccentCard<Content: View>: View {
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        content
            .padding(SKSpacing.lg)
            .background(
                RoundedRectangle(cornerRadius: SKShape.large)
                    .fill(Color.skSurface.opacity(0.96))
                    .overlay(
                        RoundedRectangle(cornerRadius: SKShape.large)
                            .stroke(Color.skOutline.opacity(0.32), lineWidth: 1)
                    )
            )
    }
}

// MARK: - Metric Card (dashboard-style)

struct SKMetricCard: View {
    let title: String
    let value: String
    let note: String
    var icon: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: SKSpacing.sm) {
            if let icon {
                Image(systemName: icon)
                    .font(.skBodyMedium)
                    .foregroundStyle(Color.skTertiary)
            }
            Text(title)
                .font(.skLabelSmall)
                .foregroundStyle(Color.skOnSurfaceVariant)
            Text(value)
                .font(.skMoney)
                .foregroundStyle(Color.skSecondary)
            Text(note)
                .font(.skBodySmall)
                .foregroundStyle(Color.skOnSurfaceVariant)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(SKSpacing.lg)
        .background(
            RoundedRectangle(cornerRadius: SKShape.large)
                .fill(Color.skSurface.opacity(0.96))
                .overlay(
                    RoundedRectangle(cornerRadius: SKShape.large)
                        .stroke(Color.skOutline.opacity(0.32), lineWidth: 1)
                )
        )
    }
}

// MARK: - Brick Button (primary action)

struct BrickButton: View {
    let title: String
    let action: () -> Void
    var isLoading: Bool = false
    var isDisabled: Bool = false
    var fullWidth: Bool = true
    var accessibilityId: String? = nil

    var body: some View {
        Button(action: action) {
            Group {
                if isLoading {
                    ProgressView()
                        .tint(.skOnPrimary)
                } else {
                    Text(title)
                        .font(.skLabelLarge)
                }
            }
            .foregroundStyle(isDisabled ? Color.skOnPrimary.opacity(0.5) : .skOnPrimary)
            .frame(maxWidth: fullWidth ? .infinity : nil)
            .frame(minHeight: 44)
            .padding(.horizontal, SKSpacing.lg)
        }
        .background(
            RoundedRectangle(cornerRadius: SKShape.medium)
                .fill(isDisabled ? Color.skPrimary.opacity(0.5) : .skPrimary)
        )
        .disabled(isDisabled || isLoading)
        .accessibilityIdentifier(accessibilityId ?? "")
    }
}

// MARK: - Soft Button (secondary action)

struct SoftButton: View {
    let title: String
    let action: () -> Void
    var role: ButtonRole? = nil
    var fullWidth: Bool = false
    var accessibilityId: String? = nil

    private var foregroundColor: Color {
        role == .destructive ? .skError : .skOnSurface
    }

    private var borderColor: Color {
        role == .destructive ? .skError.opacity(0.5) : .skOutline
    }

    var body: some View {
        Button(role: role, action: action) {
            Text(title)
                .font(.skLabelLarge)
                .foregroundStyle(foregroundColor)
                .frame(maxWidth: fullWidth ? .infinity : nil)
                .frame(minHeight: 44)
                .padding(.horizontal, SKSpacing.lg)
        }
        .background(
            RoundedRectangle(cornerRadius: SKShape.medium)
                .stroke(borderColor, lineWidth: 1)
        )
        .accessibilityIdentifier(accessibilityId ?? "")
    }
}

// MARK: - Selection Pill

struct SelectionPill: View {
    let title: String
    let isSelected: Bool
    let action: () -> Void
    var accessibilityId: String? = nil

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.skLabelLarge)
                .foregroundStyle(isSelected ? Color.skOnPrimary : Color.skOnSurfaceVariant)
                .padding(.horizontal, SKSpacing.lg)
                .padding(.vertical, SKSpacing.sm)
                .background(
                    RoundedRectangle(cornerRadius: SKShape.medium)
                        .fill(isSelected ? Color.skPrimary : .clear)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: SKShape.medium)
                        .stroke(isSelected ? Color.clear : Color.skOutline, lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(accessibilityId ?? "")
    }
}

// MARK: - Screen Header

struct ScreenHeader: View {
    let title: String
    var subtitle: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: SKSpacing.xs) {
            Text(title)
                .font(.skHeadlineLarge)
                .foregroundStyle(Color.skOnBackground)
            if let subtitle {
                Text(subtitle)
                    .font(.skBodyMedium)
                    .foregroundStyle(Color.skOnSurfaceVariant)
            }
        }
    }
}

// MARK: - Section Title

struct SectionTitle: View {
    let title: String
    var subtitle: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: SKSpacing.xs) {
            Text(title)
                .font(.skTitleMedium)
                .foregroundStyle(Color.skOnBackground)
            if let subtitle {
                Text(subtitle)
                    .font(.skBodySmall)
                    .foregroundStyle(Color.skOnSurfaceVariant)
            }
        }
    }
}

// MARK: - Status Banner

enum StatusBannerKind {
    case info, success, warning, error
}

struct StatusBanner: View {
    let message: String
    var kind: StatusBannerKind = .info

    private var backgroundColor: Color {
        switch kind {
        case .info: return .skSurfaceVariant
        case .success: return .skSuccess.opacity(0.15)
        case .warning: return .skWarning.opacity(0.15)
        case .error: return .skError.opacity(0.15)
        }
    }

    private var borderColor: Color {
        switch kind {
        case .info: return .skOutline
        case .success: return .skSuccess.opacity(0.4)
        case .warning: return .skWarning.opacity(0.4)
        case .error: return .skError.opacity(0.4)
        }
    }

    private var textColor: Color {
        switch kind {
        case .info: return .skOnSurface
        case .success: return .skSuccess
        case .warning: return .skWarning
        case .error: return .skError
        }
    }

    var body: some View {
        Text(message)
            .font(.skBodySmall)
            .foregroundStyle(textColor)
            .padding(.horizontal, SKSpacing.lg)
            .padding(.vertical, SKSpacing.md)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: SKShape.large)
                    .fill(backgroundColor)
                    .overlay(
                        RoundedRectangle(cornerRadius: SKShape.large)
                            .stroke(borderColor, lineWidth: 1)
                    )
            )
    }
}

// MARK: - Styled Text Field

struct SKTextField: View {
    let label: String
    @Binding var text: String
    var isEmail: Bool = false
    var isDecimal: Bool = false
    var isPhone: Bool = false
    var isMultiLine: Bool = false
    var accessibilityId: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: SKSpacing.xs) {
            Text(label)
                .font(.skLabelSmall)
                .foregroundStyle(Color.skOnSurfaceVariant)
            if isMultiLine {
                TextField("", text: $text, axis: .vertical)
                    .font(.skBodyLarge)
                    .foregroundStyle(Color.skOnSurface)
                    .lineLimit(3...6)
                    .padding(SKSpacing.md)
                    .background(fieldBackground)
                    .accessibilityIdentifier(accessibilityId ?? "")
            } else {
                TextField("", text: $text)
                    .font(.skBodyLarge)
                    .foregroundStyle(Color.skOnSurface)
                    .padding(SKSpacing.md)
                    .background(fieldBackground)
                    .keyboardType(isEmail ? .emailAddress : isDecimal ? .decimalPad : isPhone ? .phonePad : .default)
                    .textInputAutocapitalization(isEmail ? .never : .sentences)
                    .autocorrectionDisabled(isEmail)
                    .accessibilityIdentifier(accessibilityId ?? "")
            }
        }
    }

    private var fieldBackground: some View {
        RoundedRectangle(cornerRadius: SKShape.small)
            .fill(Color.skSurfaceVariant)
            .overlay(
                RoundedRectangle(cornerRadius: SKShape.small)
                    .stroke(Color.skOutline.opacity(0.5), lineWidth: 1)
            )
    }
}

// MARK: - Styled Secure Field

struct SKSecureField: View {
    let label: String
    @Binding var text: String
    var accessibilityId: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: SKSpacing.xs) {
            Text(label)
                .font(.skLabelSmall)
                .foregroundStyle(Color.skOnSurfaceVariant)
            SecureField("", text: $text)
                .font(.skBodyLarge)
                .foregroundStyle(Color.skOnSurface)
                .padding(SKSpacing.md)
                .background(
                    RoundedRectangle(cornerRadius: SKShape.small)
                        .fill(Color.skSurfaceVariant)
                        .overlay(
                            RoundedRectangle(cornerRadius: SKShape.small)
                                .stroke(Color.skOutline.opacity(0.5), lineWidth: 1)
                        )
                )
                .accessibilityIdentifier(accessibilityId ?? "")
        }
    }
}

// MARK: - Screen Column (scrollable content wrapper)

struct ScreenColumn<Content: View>: View {
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: SKSpacing.md) {
                content
            }
            .padding(.horizontal, SKSpacing.lg)
            .padding(.vertical, SKSpacing.lg)
        }
        .background(ShopkeeperBackground())
    }
}

// MARK: - Floating Tab Bar Item

struct SKTabItem: Identifiable {
    let id: String
    let title: String
    let icon: String
}

struct FloatingTabBar: View {
    let items: [SKTabItem]
    @Binding var selectedTab: String

    var body: some View {
        HStack(spacing: 0) {
            ForEach(items) { item in
                Button {
                    withAnimation(.easeInOut(duration: 0.2)) {
                        selectedTab = item.id
                    }
                } label: {
                    VStack(spacing: SKSpacing.xs) {
                        Image(systemName: item.icon)
                            .font(.system(size: 18))
                        Text(item.title)
                            .font(.system(size: 10, weight: .medium))
                    }
                    .foregroundStyle(selectedTab == item.id ? Color.skPrimary : Color.skOnSurfaceVariant)
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, SKSpacing.sm)
                    .background(
                        selectedTab == item.id
                            ? RoundedRectangle(cornerRadius: SKShape.medium)
                                .fill(Color.skPrimary.opacity(0.14))
                            : nil
                    )
                }
                .buttonStyle(.plain)
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(item.title)
                .accessibilityValue(selectedTab == item.id ? "selected" : "")
                .accessibilityIdentifier("nav.tab.\(item.id)")
            }
        }
        .padding(.horizontal, SKSpacing.sm)
        .padding(.vertical, SKSpacing.sm)
        .background(
            RoundedRectangle(cornerRadius: SKShape.extraLarge)
                .fill(Color.skSurface.opacity(0.82))
                .overlay(
                    RoundedRectangle(cornerRadius: SKShape.extraLarge)
                        .stroke(Color.skOutline.opacity(0.24), lineWidth: 1)
                )
                .shadow(color: .black.opacity(0.3), radius: 12, y: 4)
        )
        .padding(.horizontal, SKSpacing.lg)
        .padding(.bottom, SKSpacing.md)
    }
}

// MARK: - Inline Metric Row (for use inside cards/lists)

struct SKMetricRow: View {
    let title: String
    let value: String
    var note: String? = nil

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: SKSpacing.xs) {
                Text(title)
                    .font(.skBodyMedium)
                    .foregroundStyle(Color.skOnSurfaceVariant)
                if let note {
                    Text(note)
                        .font(.skBodySmall)
                        .foregroundStyle(Color.skOnSurfaceVariant.opacity(0.7))
                }
            }
            Spacer()
            Text(value)
                .font(.skMoney)
                .foregroundStyle(Color.skSecondary)
        }
    }
}

// MARK: - Staggered Appearance Modifier

struct StaggeredAppearance: ViewModifier {
    let index: Int
    @State private var appeared = false

    func body(content: Content) -> some View {
        content
            .offset(y: appeared ? 0 : 20)
            .opacity(appeared ? 1 : 0)
            .onAppear {
                withAnimation(.easeOut(duration: 0.35).delay(Double(index) * 0.05)) {
                    appeared = true
                }
            }
    }
}

extension View {
    func staggeredAppearance(index: Int) -> some View {
        modifier(StaggeredAppearance(index: index))
    }
}

// MARK: - Haptic Feedback Extension

extension View {
    func onTapWithHaptic(_ style: UIImpactFeedbackGenerator.FeedbackStyle = .medium, action: @escaping () -> Void) -> some View {
        self.simultaneousGesture(
            TapGesture().onEnded {
                let generator = UIImpactFeedbackGenerator(style: style)
                generator.impactOccurred()
                action()
            }
        )
    }
}

// MARK: - Sale Celebration Overlay

struct SaleCelebrationOverlay: View {
    @Binding var isShowing: Bool
    @State private var checkmarkScale: CGFloat = 0.3
    @State private var checkmarkOpacity: Double = 0
    @State private var textOpacity: Double = 0

    var body: some View {
        if isShowing {
            ZStack {
                Color.black.opacity(0.6)
                    .ignoresSafeArea()

                VStack(spacing: SKSpacing.xl) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 80))
                        .foregroundStyle(Color.skSuccess)
                        .scaleEffect(checkmarkScale)
                        .opacity(checkmarkOpacity)

                    Text("Sale Complete!")
                        .font(.skHeadlineLarge)
                        .foregroundStyle(Color.skOnBackground)
                        .opacity(textOpacity)
                }
            }
            .onAppear {
                withAnimation(.spring(response: 0.5, dampingFraction: 0.6)) {
                    checkmarkScale = 1.0
                    checkmarkOpacity = 1.0
                }
                withAnimation(.easeIn(duration: 0.3).delay(0.3)) {
                    textOpacity = 1.0
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) {
                    withAnimation(.easeOut(duration: 0.25)) {
                        isShowing = false
                    }
                }
            }
        }
    }
}

// MARK: - Skeleton Loading View

struct SkeletonLoadingView: View {
    var height: CGFloat = 20
    @State private var shimmerOffset: CGFloat = -1

    var body: some View {
        GeometryReader { geometry in
            RoundedRectangle(cornerRadius: SKShape.small)
                .fill(Color.skSurfaceVariant)
                .overlay(
                    LinearGradient(
                        colors: [
                            Color.skSurfaceVariant,
                            Color.skOutline.opacity(0.4),
                            Color.skSurfaceVariant
                        ],
                        startPoint: .leading,
                        endPoint: .trailing
                    )
                    .frame(width: geometry.size.width * 0.6)
                    .offset(x: shimmerOffset * geometry.size.width)
                    .clipShape(RoundedRectangle(cornerRadius: SKShape.small))
                )
                .clipped()
        }
        .frame(height: height)
        .onAppear {
            withAnimation(.linear(duration: 1.5).repeatForever(autoreverses: false)) {
                shimmerOffset = 1.5
            }
        }
    }
}
