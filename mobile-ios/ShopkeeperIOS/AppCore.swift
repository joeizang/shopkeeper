import Foundation
import SwiftUI
import UIKit
@preconcurrency import Vision

enum ShopRole: String, Codable, CaseIterable, Identifiable {
    case owner = "Owner"
    case shopManager = "ShopManager"
    case salesperson = "Salesperson"

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .owner: return "Owner"
        case .shopManager: return "Shop Manager"
        case .salesperson: return "Salesperson"
        }
    }
}

enum PaymentMethodOption: Int, CaseIterable, Identifiable, Codable {
    case cash = 1
    case bankTransfer = 2
    case pos = 3

    var id: Int { rawValue }

    var title: String {
        switch self {
        case .cash: return "Cash"
        case .bankTransfer: return "Bank Transfer"
        case .pos: return "POS"
        }
    }
}

enum ItemTypeOption: Int, CaseIterable, Identifiable, Codable {
    case newItem = 1
    case used = 2

    var id: Int { rawValue }

    var title: String {
        switch self {
        case .newItem: return "New"
        case .used: return "Used"
        }
    }
}

enum ConditionGradeOption: Int, CaseIterable, Identifiable, Codable {
    case a = 1
    case b = 2
    case c = 3

    var id: Int { rawValue }
    var title: String { String(describing: self).uppercased() }
}

enum ReportKind: String, CaseIterable, Identifiable {
    case inventory
    case sales
    case profitLoss
    case creditors

    var id: String { rawValue }

    var apiName: String {
        switch self {
        case .inventory: return "inventory"
        case .sales: return "sales"
        case .profitLoss: return "profit-loss"
        case .creditors: return "creditors"
        }
    }

    var title: String {
        switch self {
        case .inventory: return "Inventory"
        case .sales: return "Sales"
        case .profitLoss: return "Profit & Loss"
        case .creditors: return "Creditors"
        }
    }
}

enum ReportFormat: String, CaseIterable, Identifiable {
    case pdf
    case spreadsheet

    var id: String { rawValue }

    var title: String {
        switch self {
        case .pdf: return "PDF"
        case .spreadsheet: return "Spreadsheet"
        }
    }
}

enum AppThemePreference: String, CaseIterable, Identifiable {
    case system
    case light
    case dark

    var id: String { rawValue }

    var title: String { rawValue.capitalized }

    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return .light
        case .dark: return .dark
        }
    }
}

struct RoleCapabilities {
    let role: ShopRole

    var canManageInventory: Bool { role == .owner || role == .shopManager }
    var canManageSales: Bool { true }
    var canViewReports: Bool { role == .owner || role == .shopManager }
    var canViewProfitLoss: Bool { role == .owner }
    var canManageShopSettings: Bool { role == .owner }
    var canManageStaff: Bool { role == .owner }
    var canManageExpenses: Bool { role == .owner }
}

struct AppConfig {
    static var apiBaseURL: URL {
        let raw = (Bundle.main.object(forInfoDictionaryKey: "API_BASE_URL") as? String) ?? "http://127.0.0.1:5057"
        return URL(string: raw.hasSuffix("/") ? raw : raw + "/") ?? URL(string: "http://127.0.0.1:5057/")!
    }
}

struct LoginRequest: Encodable {
    let login: String
    let password: String
    let shopId: String?
}

struct RegisterOwnerRequest: Encodable {
    let fullName: String
    let email: String?
    let phone: String?
    let password: String
    let shopName: String
    let vatEnabled: Bool
    let vatRate: Double
}

struct AuthResponse: Decodable {
    let accessToken: String
    let refreshToken: String
    let accessTokenExpiresAtUtc: String
    let shopId: String
    let role: String
}

struct ShopView: Decodable, Identifiable {
    let id: String
    let name: String
    let code: String
    let vatEnabled: Bool
    let vatRate: Double
    let defaultDiscountPercent: Double
    let role: String
    let rowVersionBase64: String

    var shopRole: ShopRole { ShopRole(rawValue: role) ?? .owner }
}

struct AccountProfile: Decodable {
    let userId: String
    let fullName: String
    let email: String?
    let phone: String?
    let avatarUrl: String?
    let preferredLanguage: String?
    let timezone: String?
    let createdAtUtc: String
}

struct UpdateAccountProfileRequest: Encodable {
    let fullName: String
    let phone: String?
    let avatarUrl: String?
    let preferredLanguage: String?
    let timezone: String?
}

struct UpdateShopSettingsRequest: Encodable {
    let vatEnabled: Bool
    let vatRate: Double
    let defaultDiscountPercent: Double
    let rowVersionBase64: String?
}

struct SessionView: Decodable, Identifiable {
    let sessionId: String
    let shopId: String
    let role: String
    let deviceId: String?
    let deviceName: String?
    let createdAtUtc: String
    let expiresAtUtc: String
    let lastSeenAtUtc: String?
    let isRevoked: Bool

    var id: String { sessionId }
}

struct LinkedIdentityView: Decodable, Identifiable {
    let provider: String
    let providerSubject: String
    let email: String?
    let emailVerified: Bool
    let createdAtUtc: String
    let lastUsedAtUtc: String

    var id: String { "\(provider)-\(providerSubject)" }
}

struct StaffMembershipView: Decodable, Identifiable {
    let staffId: String
    let userId: String
    let fullName: String
    let email: String?
    let phone: String?
    let role: String
    let isActive: Bool
    let createdAtUtc: String

    var id: String { staffId }
    var shopRole: ShopRole { ShopRole(rawValue: role) ?? .salesperson }
}

struct InviteStaffRequest: Encodable {
    let fullName: String
    let email: String?
    let phone: String?
    let temporaryPassword: String
    let role: String
}

struct UpdateStaffMembershipRequest: Encodable {
    let role: String
    let isActive: Bool
}

struct InventoryItemResponse: Decodable, Identifiable {
    let id: String
    let productName: String
    let modelNumber: String?
    let serialNumber: String?
    let quantity: Int
    let expiryDate: String?
    let costPrice: Double
    let sellingPrice: Double
    let itemType: Int
    let conditionGrade: Int?
    let conditionNotes: String?
    let photoUris: [String]
    let rowVersionBase64: String
}

struct CreateInventoryItemRequest: Encodable {
    let productName: String
    let modelNumber: String?
    let serialNumber: String?
    let quantity: Int
    let expiryDate: String?
    let costPrice: Double
    let sellingPrice: Double
    let itemType: Int
    let conditionGrade: Int?
    let conditionNotes: String?
}

struct UpdateInventoryItemRequest: Encodable {
    let productName: String?
    let modelNumber: String?
    let serialNumber: String?
    let quantity: Int?
    let expiryDate: String?
    let costPrice: Double?
    let sellingPrice: Double?
    let itemType: Int?
    let conditionGrade: Int?
    let conditionNotes: String?
    let rowVersionBase64: String?
}

struct AddItemPhotoRequest: Encodable {
    let photoUri: String
}

struct SaleLineDraft: Identifiable, Hashable, Codable {
    let id = UUID()
    let inventoryItemId: String
    let productName: String
    var quantity: Int
    var unitPrice: Double

    var lineTotal: Double { Double(quantity) * unitPrice }
}

struct SaleLineRequest: Encodable {
    let inventoryItemId: String
    let quantity: Int
    let unitPrice: Double
}

struct SalePaymentRequest: Encodable, Decodable, Identifiable, Hashable {
    let method: Int
    let amount: Double
    let reference: String?

    var id: String { "\(method)-\(amount)-\(reference ?? "")" }

    var paymentMethod: PaymentMethodOption {
        PaymentMethodOption(rawValue: method) ?? .cash
    }
}

struct CreateSaleRequest: Encodable {
    let customerName: String?
    let customerPhone: String?
    let discountAmount: Double
    let isCredit: Bool
    let dueDateUtc: String?
    let lines: [SaleLineRequest]
    let initialPayments: [SalePaymentRequest]?
}

struct CreateSaleResponse: Decodable {
    let id: String
    let saleNumber: String
    let totalAmount: Double
    let outstandingAmount: Double
}

struct SaleDetailLine: Decodable, Identifiable {
    let id: String
    let inventoryItemId: String
    let productNameSnapshot: String
    let quantity: Int
    let unitPrice: Double
    let lineTotal: Double
}

struct SaleDetailPayment: Decodable, Identifiable {
    let id: String
    let method: String
    let amount: Double
    let reference: String?
    let createdAtUtc: String
}

struct SaleDetailResponse: Decodable, Identifiable {
    let id: String
    let saleNumber: String
    let customerName: String?
    let customerPhone: String?
    let subtotal: Double
    let vatAmount: Double
    let discountAmount: Double
    let totalAmount: Double
    let outstandingAmount: Double
    let status: String
    let isCredit: Bool
    let dueDateUtc: String?
    let isVoided: Bool
    let updatedAtUtc: String
    let lines: [SaleDetailLine]
    let payments: [SaleDetailPayment]
}

struct ReceiptLineView: Decodable, Identifiable {
    var id: String { "\(productName)-\(quantity)-\(unitPrice)" }
    let productName: String
    let quantity: Int
    let unitPrice: Double
    let lineTotal: Double
}

struct ReceiptView: Decodable {
    let saleId: String
    let saleNumber: String
    let createdAtUtc: String
    let shopName: String
    let customerName: String?
    let subtotal: Double
    let vatAmount: Double
    let discountAmount: Double
    let totalAmount: Double
    let paidAmount: Double
    let outstandingAmount: Double
    let lines: [ReceiptLineView]
    let payments: [SalePaymentRequest]
}

struct CreditAccountView: Decodable, Identifiable {
    let id: String
    let saleId: String
    let dueDateUtc: String
    let outstandingAmount: Double
    let status: String
}

struct CreditRepaymentView: Decodable, Identifiable {
    let id: String
    let amount: Double
    let method: Int
    let reference: String?
    let notes: String?
    let createdAtUtc: String

    var paymentMethod: PaymentMethodOption {
        PaymentMethodOption(rawValue: method) ?? .cash
    }
}

struct CreditDetailResponse: Decodable {
    let account: CreditAccountView
    let repayments: [CreditRepaymentView]
}

struct CreditRepaymentRequest: Encodable {
    let amount: Double
    let method: Int
    let reference: String?
    let notes: String?
}

struct InventoryReportRow: Decodable, Identifiable {
    let itemId: String
    let productName: String
    let quantity: Int
    let costPrice: Double
    let sellingPrice: Double
    let costValue: Double
    let sellingValue: Double
    let expiryDate: String?

    var id: String { itemId }
}

struct InventoryReportResponse: Decodable {
    let generatedAtUtc: String
    let totalProducts: Int
    let totalUnits: Int
    let lowStockItems: Int
    let totalCostValue: Double
    let totalSellingValue: Double
    let items: [InventoryReportRow]
}

struct SalesDailySummaryRow: Decodable, Identifiable {
    var id: String { date }
    let date: String
    let salesCount: Int
    let revenue: Double
    let outstanding: Double
}

struct SalesPaymentSummaryRow: Decodable, Identifiable {
    var id: String { method }
    let method: String
    let amount: Double
}

struct SalesReportResponse: Decodable {
    let generatedAtUtc: String
    let fromUtc: String
    let toUtc: String
    let salesCount: Int
    let revenue: Double
    let vatAmount: Double
    let discountAmount: Double
    let outstandingAmount: Double
    let daily: [SalesDailySummaryRow]
    let payments: [SalesPaymentSummaryRow]
}

struct ExpenseCategorySummaryRow: Decodable, Identifiable {
    var id: String { category }
    let category: String
    let amount: Double
}

struct ProfitLossReportResponse: Decodable {
    let generatedAtUtc: String
    let fromUtc: String
    let toUtc: String
    let revenue: Double
    let cogs: Double
    let grossProfit: Double
    let expenses: Double
    let netProfitLoss: Double
    let expenseBreakdown: [ExpenseCategorySummaryRow]
}

struct CreditorReportRow: Decodable, Identifiable {
    let creditAccountId: String
    let saleId: String
    let saleNumber: String
    let customerName: String
    let itemsSummary: String
    let dueDateUtc: String
    let daysOverdue: Int
    let outstandingAmount: Double
    let status: String

    var id: String { creditAccountId }
}

struct CreditorsReportResponse: Decodable {
    let generatedAtUtc: String
    let fromUtc: String?
    let toUtc: String?
    let openCredits: Int
    let totalOutstanding: Double
    let credits: [CreditorReportRow]
}

struct CreateExpenseRequest: Encodable {
    let title: String
    let category: String
    let amount: Double
    let expenseDateUtc: String
    let notes: String?
}

struct UpdateExpenseRequest: Encodable {
    let title: String?
    let category: String?
    let amount: Double?
    let expenseDateUtc: String?
    let notes: String?
    let rowVersionBase64: String?
}

struct ExpenseView: Decodable, Identifiable {
    let id: String
    let title: String
    let category: String
    let amount: Double
    let expenseDateUtc: String
    let notes: String?
    let createdAtUtc: String
    let rowVersionBase64: String
}

struct QueueReportJobRequest: Encodable {
    let reportType: String
    let format: String
    let fromUtc: String?
    let toUtc: String?
}

struct ReportJobView: Decodable, Identifiable {
    let id: String
    let reportType: String
    let format: String
    let status: String
    let filterJson: String?
    let reportFileId: String?
    let requestedAtUtc: String
    let completedAtUtc: String?
    let failureReason: String?
}

struct ReportFileView: Decodable, Identifiable {
    let id: String
    let reportType: String
    let format: String
    let fileName: String
    let contentType: String
    let byteLength: Int64
    let createdAtUtc: String
}

struct SyncPushChange: Encodable, Decodable, Identifiable {
    let deviceId: String
    let entityName: String
    let entityId: String
    let operation: Int
    let payloadJson: String
    let clientUpdatedAtUtc: String
    let rowVersionBase64: String?

    var id: String { "\(entityName)-\(entityId)-\(clientUpdatedAtUtc)" }
}

struct SyncPushRequest: Encodable {
    let changes: [SyncPushChange]
}

struct SyncConflictView: Decodable, Identifiable {
    let changeId: String
    let entityName: String
    let entityId: String
    let reason: String
    let serverPayloadJson: String?
    let serverRowVersionBase64: String?

    var id: String { changeId }
}

struct SyncPushResponse: Decodable {
    let acceptedCount: Int
    let conflicts: [SyncConflictView]
}

struct SyncPullRequest: Encodable {
    let deviceId: String
    let sinceUtc: String?
}

struct SyncPullResponse: Decodable {
    let serverTimestampUtc: String
    let changes: [SyncPushChange]
}

struct ReportSummary {
    let title: String
    let lines: [String]
}

struct DashboardSnapshot {
    let inventoryItems: Int
    let openCredits: Int
    let totalInventoryUnits: Int
    let lowStockItems: Int
    let totalInventoryWorth: Double
    let todaysRevenue: Double
    let todaysSalesCount: Int
    let outstandingCredit: Double
    let pendingReportJobs: Int
    let openConflicts: Int
}

struct SyncSummary {
    let lastPulledAtUtc: String?
    let lastPushAccepted: Int
    let lastPullChanges: Int
    let lastConflictCount: Int
}

struct InventoryFormState: Codable, Equatable {
    var productName = ""
    var modelNumber = ""
    var serialNumber = ""
    var quantity = "1"
    var expiryDate: Date? = nil
    var costPrice = "0"
    var sellingPrice = "0"
    var itemType: ItemTypeOption = .newItem
    var conditionGrade: ConditionGradeOption? = nil
    var conditionNotes = ""
    var photoUris: [String] = []

    init() {}

    init(item: InventoryItemResponse) {
        productName = item.productName
        modelNumber = item.modelNumber ?? ""
        serialNumber = item.serialNumber ?? ""
        quantity = "\(item.quantity)"
        expiryDate = parseBackendDate(item.expiryDate)
        costPrice = decimalString(item.costPrice)
        sellingPrice = decimalString(item.sellingPrice)
        itemType = ItemTypeOption(rawValue: item.itemType) ?? .newItem
        conditionGrade = item.conditionGrade.flatMap { ConditionGradeOption(rawValue: $0) }
        conditionNotes = item.conditionNotes ?? ""
        photoUris = item.photoUris
    }
}

struct ExpenseFormState {
    var title = ""
    var category = "Operations"
    var amount = ""
    var expenseDate = Date()
    var notes = ""

    init() {}

    init(expense: ExpenseView) {
        title = expense.title
        category = expense.category
        amount = decimalString(expense.amount)
        expenseDate = parseBackendDateTime(expense.expenseDateUtc) ?? Date()
        notes = expense.notes ?? ""
    }
}

enum APIError: LocalizedError {
    case invalidResponse
    case message(String)

    var errorDescription: String? {
        switch self {
        case .invalidResponse:
            return "Invalid server response."
        case .message(let message):
            return message
        }
    }
}

private struct AnyEncodable: Encodable {
    private let encodeBlock: (Encoder) throws -> Void

    init<T: Encodable>(_ value: T) {
        encodeBlock = value.encode
    }

    func encode(to encoder: Encoder) throws {
        try encodeBlock(encoder)
    }
}

final class APIClient {
    static let shared = APIClient()

    private let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.keyDecodingStrategy = .useDefaultKeys
        return decoder
    }()
    private let encoder = JSONEncoder()

    private init() {}

    func request<T: Decodable>(_ path: String, method: String = "GET", body: Encodable? = nil, accessToken: String? = nil) async throws -> T {
        let response = try await send(path: path, method: method, body: body, accessToken: accessToken)
        return try decoder.decode(T.self, from: response)
    }

    func requestVoid(_ path: String, method: String = "POST", body: Encodable? = nil, accessToken: String? = nil) async throws {
        _ = try await send(path: path, method: method, body: body, accessToken: accessToken)
    }

    func requestData(_ path: String, method: String = "GET", body: Encodable? = nil, accessToken: String? = nil) async throws -> Data {
        try await send(path: path, method: method, body: body, accessToken: accessToken)
    }

    private func send(path: String, method: String, body: Encodable?, accessToken: String?) async throws -> Data {
        guard let fullURL = URL(string: path, relativeTo: AppConfig.apiBaseURL)?.absoluteURL else {
            throw APIError.message("Invalid request URL.")
        }
        var request = URLRequest(url: fullURL)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")

        if let accessToken {
            request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        }

        if let body {
            request.httpBody = try encoder.encode(AnyEncodable(body))
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw APIError.invalidResponse
        }

        guard (200..<300).contains(http.statusCode) else {
            if let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                if let message = json["message"] as? String {
                    throw APIError.message(message)
                }
                if let title = json["title"] as? String {
                    throw APIError.message(title)
                }
                if let errors = json["errors"] as? [String: [String]], let first = errors.values.first?.first {
                    throw APIError.message(first)
                }
            }

            throw APIError.message(String(data: data, encoding: .utf8) ?? "HTTP \(http.statusCode)")
        }

        return data
    }
}

@MainActor
final class SessionStore: ObservableObject {
    @Published var auth: AuthResponse?
    @Published var shops: [ShopView] = []
    @Published var currentShop: ShopView?
    @Published var profile: AccountProfile?
    @Published var inventory: [InventoryItemResponse] = []
    @Published var credits: [CreditAccountView] = []
    @Published var staffMembers: [StaffMembershipView] = []
    @Published var sessions: [SessionView] = []
    @Published var linkedIdentities: [LinkedIdentityView] = []
    @Published var reportJobs: [ReportJobView] = []
    @Published var reportFiles: [ReportFileView] = []
    @Published var expenses: [ExpenseView] = []
    @Published var todaysSalesReport: SalesReportResponse?
    @Published var selectedCreditDetail: CreditDetailResponse?
    @Published var recentSales: [SaleDetailResponse] = []
    @Published var lastReceipt: ReceiptView?
    @Published var syncSummary = SyncSummary(lastPulledAtUtc: nil, lastPushAccepted: 0, lastPullChanges: 0, lastConflictCount: 0)
    @Published var syncConflicts: [SyncConflictView] = []
    @Published var statusMessage: String?
    @Published var isBusy = false
    @Published var themePreference: AppThemePreference = .system

    private let api = APIClient.shared
    private let defaults = UserDefaults.standard

    var isAuthenticated: Bool { auth != nil }
    var role: ShopRole { currentShop?.shopRole ?? ShopRole(rawValue: auth?.role ?? "") ?? .owner }
    var capabilities: RoleCapabilities { RoleCapabilities(role: role) }
    var themeColorScheme: ColorScheme? { themePreference.colorScheme }

    var dashboard: DashboardSnapshot {
        let sales = todaysSalesReport
        return DashboardSnapshot(
            inventoryItems: inventory.count,
            openCredits: credits.filter { !$0.status.lowercased().contains("settled") && $0.outstandingAmount > 0 }.count,
            totalInventoryUnits: inventory.reduce(0) { $0 + $1.quantity },
            lowStockItems: inventory.filter { $0.quantity <= 2 }.count,
            totalInventoryWorth: inventory.reduce(0) { $0 + ($1.costPrice * Double($1.quantity)) },
            todaysRevenue: sales?.revenue ?? 0,
            todaysSalesCount: sales?.salesCount ?? 0,
            outstandingCredit: credits.reduce(0) { $0 + $1.outstandingAmount },
            pendingReportJobs: reportJobs.filter { ["Pending", "InProgress"].contains($0.status) }.count,
            openConflicts: syncSummary.lastConflictCount
        )
    }

    func bootstrap() async {
        restoreTheme()
        restoreSession()
        guard isAuthenticated else { return }
        await refreshAll()
    }

    func setTheme(_ theme: AppThemePreference) {
        themePreference = theme
        defaults.set(theme.rawValue, forKey: "ios_theme_preference")
    }

    func login(login: String, password: String) async {
        await perform { [self] in
            let response: AuthResponse = try await api.request(
                "api/v1/auth/login",
                method: "POST",
                body: LoginRequest(login: login, password: password, shopId: nil)
            )
            auth = response
            persistSession(response)
            await refreshAll()
            statusMessage = nil
        }
    }

    func registerOwner(fullName: String, email: String, password: String, shopName: String, vatEnabled: Bool, vatRate: Double) async {
        await perform { [self] in
            let response: AuthResponse = try await api.request(
                "api/v1/auth/register-owner",
                method: "POST",
                body: RegisterOwnerRequest(
                    fullName: fullName,
                    email: email,
                    phone: nil,
                    password: password,
                    shopName: shopName,
                    vatEnabled: vatEnabled,
                    vatRate: vatRate
                )
            )
            auth = response
            persistSession(response)
            await refreshAll()
            statusMessage = nil
        }
    }

    func logout() {
        auth = nil
        shops = []
        currentShop = nil
        profile = nil
        inventory = []
        credits = []
        staffMembers = []
        sessions = []
        linkedIdentities = []
        reportJobs = []
        reportFiles = []
        expenses = []
        todaysSalesReport = nil
        selectedCreditDetail = nil
        recentSales = []
        lastReceipt = nil
        syncSummary = SyncSummary(lastPulledAtUtc: nil, lastPushAccepted: 0, lastPullChanges: 0, lastConflictCount: 0)
        removeStoredSession()
    }

    func refreshAll() async {
        guard let accessToken = auth?.accessToken else { return }

        await perform { [self] in
            async let loadedShops: [ShopView] = api.request("api/v1/shops/me", accessToken: accessToken)
            async let loadedProfile: AccountProfile = api.request("api/v1/account/me", accessToken: accessToken)
            async let loadedInventory: [InventoryItemResponse] = capabilities.canManageInventory
                ? api.request("api/v1/inventory/items", accessToken: accessToken)
                : []
            async let loadedCredits: [CreditAccountView] = capabilities.canManageSales
                ? api.request("api/v1/credits", accessToken: accessToken)
                : []
            async let loadedSessions: [SessionView] = api.request("api/v1/account/sessions", accessToken: accessToken)
            async let loadedIdentities: [LinkedIdentityView] = api.request("api/v1/account/linked-identities", accessToken: accessToken)
            async let loadedReportJobs: [ReportJobView] = capabilities.canViewReports
                ? api.request("api/v1/reports/jobs", accessToken: accessToken)
                : []
            async let loadedReportFiles: [ReportFileView] = capabilities.canViewReports
                ? api.request("api/v1/reports/files", accessToken: accessToken)
                : []
            async let loadedExpenses: [ExpenseView] = capabilities.canManageExpenses
                ? api.request("api/v1/expenses", accessToken: accessToken)
                : []
            async let salesReport: SalesReportResponse? = capabilities.canManageSales
                ? api.request("api/v1/reports/sales?from=\(isoDay(Date()))&to=\(isoDay(Date()))", accessToken: accessToken)
                : nil

            let resolvedShops = try await loadedShops
            shops = resolvedShops
            profile = try await loadedProfile
            inventory = try await loadedInventory
            credits = try await loadedCredits
            sessions = try await loadedSessions
            linkedIdentities = try await loadedIdentities
            reportJobs = try await loadedReportJobs
            reportFiles = try await loadedReportFiles
            expenses = try await loadedExpenses
            todaysSalesReport = try await salesReport

            let selectedShopId = auth?.shopId ?? defaults.string(forKey: "ios_shop_id")
            currentShop = resolvedShops.first(where: { $0.id == selectedShopId }) ?? resolvedShops.first
            if let currentShop {
                defaults.set(currentShop.id, forKey: "ios_shop_id")
            }

            if capabilities.canManageStaff, let shop = currentShop {
                staffMembers = try await api.request("api/v1/shops/\(shop.id)/staff", accessToken: accessToken)
            } else {
                staffMembers = []
            }
        }
    }

    func refreshInventory() async {
        guard let accessToken = auth?.accessToken, capabilities.canManageInventory else { return }
        await perform { [self] in
            inventory = try await api.request("api/v1/inventory/items", accessToken: accessToken)
        }
    }

    func createInventoryItem(_ form: InventoryFormState) async -> InventoryItemResponse? {
        guard let accessToken = auth?.accessToken else { return nil }
        var createdItem: InventoryItemResponse?
        await perform { [self] in
            let created: InventoryItemResponse = try await api.request(
                "api/v1/inventory/items",
                method: "POST",
                body: CreateInventoryItemRequest(
                    productName: form.productName.trimmingCharacters(in: .whitespacesAndNewlines),
                    modelNumber: nullable(form.modelNumber),
                    serialNumber: nullable(form.serialNumber),
                    quantity: Int(form.quantity) ?? 1,
                    expiryDate: form.expiryDate.map { backendDate($0) },
                    costPrice: Double(form.costPrice) ?? 0,
                    sellingPrice: Double(form.sellingPrice) ?? 0,
                    itemType: form.itemType.rawValue,
                    conditionGrade: form.conditionGrade?.rawValue,
                    conditionNotes: nullable(form.conditionNotes)
                ),
                accessToken: accessToken
            )
            inventory.insert(created, at: 0)
            createdItem = created
            statusMessage = "Inventory item created."
        }
        return createdItem
    }

    func updateInventoryItem(item: InventoryItemResponse, form: InventoryFormState) async -> InventoryItemResponse? {
        guard let accessToken = auth?.accessToken else { return nil }
        var updatedItem: InventoryItemResponse?
        await perform { [self] in
            let updated: InventoryItemResponse = try await api.request(
                "api/v1/inventory/items/\(item.id)",
                method: "PATCH",
                body: UpdateInventoryItemRequest(
                    productName: form.productName,
                    modelNumber: nullable(form.modelNumber),
                    serialNumber: nullable(form.serialNumber),
                    quantity: Int(form.quantity) ?? item.quantity,
                    expiryDate: form.expiryDate.map { backendDate($0) },
                    costPrice: Double(form.costPrice) ?? item.costPrice,
                    sellingPrice: Double(form.sellingPrice) ?? item.sellingPrice,
                    itemType: form.itemType.rawValue,
                    conditionGrade: form.conditionGrade?.rawValue,
                    conditionNotes: nullable(form.conditionNotes),
                    rowVersionBase64: item.rowVersionBase64
                ),
                accessToken: accessToken
            )
            inventory = inventory.map { $0.id == updated.id ? updated : $0 }
            updatedItem = updated
            statusMessage = "Inventory item updated."
        }
        return updatedItem
    }

    func deleteInventoryItem(_ item: InventoryItemResponse) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let encodedRowVersion = item.rowVersionBase64.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? item.rowVersionBase64
            try await api.requestVoid(
                "api/v1/inventory/items/\(item.id)?rowVersionBase64=\(encodedRowVersion)",
                method: "DELETE",
                accessToken: accessToken
            )
            inventory.removeAll { $0.id == item.id }
            statusMessage = "Inventory item deleted."
        }
    }

    func addInventoryPhoto(itemId: String, photoUri: String) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            _ = try await api.requestData(
                "api/v1/inventory/items/\(itemId)/photos",
                method: "POST",
                body: AddItemPhotoRequest(photoUri: photoUri),
                accessToken: accessToken
            )
            await refreshInventory()
            statusMessage = "Photo added."
        }
    }

    func updateProfile(fullName: String, phone: String, avatarUrl: String) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let updated: AccountProfile = try await api.request(
                "api/v1/account/me",
                method: "PATCH",
                body: UpdateAccountProfileRequest(
                    fullName: fullName,
                    phone: nullable(phone),
                    avatarUrl: nullable(avatarUrl),
                    preferredLanguage: profile?.preferredLanguage ?? "en",
                    timezone: profile?.timezone ?? TimeZone.current.identifier
                ),
                accessToken: accessToken
            )
            profile = updated
            statusMessage = "Profile updated."
        }
    }

    func updateShopSettings(vatEnabled: Bool, vatRate: Double, discountPercent: Double) async {
        guard let accessToken = auth?.accessToken, let shop = currentShop else { return }
        await perform { [self] in
            let updated: ShopView = try await api.request(
                "api/v1/shops/\(shop.id)/settings",
                method: "PATCH",
                body: UpdateShopSettingsRequest(
                    vatEnabled: vatEnabled,
                    vatRate: vatEnabled ? vatRate : 0,
                    defaultDiscountPercent: discountPercent,
                    rowVersionBase64: shop.rowVersionBase64
                ),
                accessToken: accessToken
            )
            currentShop = updated
            shops = shops.map { $0.id == updated.id ? updated : $0 }
            statusMessage = "Shop settings updated."
        }
    }

    func inviteStaff(fullName: String, email: String, phone: String, password: String, role: ShopRole) async {
        guard let accessToken = auth?.accessToken, let shop = currentShop else { return }
        await perform { [self] in
            let invited: StaffMembershipView = try await api.request(
                "api/v1/shops/\(shop.id)/staff/invite",
                method: "POST",
                body: InviteStaffRequest(
                    fullName: fullName.trimmingCharacters(in: .whitespacesAndNewlines),
                    email: nullable(email),
                    phone: nullable(phone),
                    temporaryPassword: password,
                    role: role.rawValue
                ),
                accessToken: accessToken
            )
            staffMembers.insert(invited, at: 0)
            statusMessage = "Staff invited."
        }
    }

    func updateStaff(_ staff: StaffMembershipView, role: ShopRole? = nil, isActive: Bool? = nil) async {
        guard let accessToken = auth?.accessToken, let shop = currentShop else { return }
        await perform { [self] in
            let updated: StaffMembershipView = try await api.request(
                "api/v1/shops/\(shop.id)/staff/\(staff.staffId)",
                method: "PATCH",
                body: UpdateStaffMembershipRequest(
                    role: (role ?? staff.shopRole).rawValue,
                    isActive: isActive ?? staff.isActive
                ),
                accessToken: accessToken
            )
            staffMembers = staffMembers.map { $0.staffId == updated.staffId ? updated : $0 }
            statusMessage = "Staff member updated."
        }
    }

    func revokeSession(_ session: SessionView) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            try await api.requestVoid("api/v1/account/sessions/\(session.sessionId)/revoke", accessToken: accessToken)
            sessions = sessions.map {
                $0.sessionId == session.sessionId
                    ? SessionView(
                        sessionId: $0.sessionId,
                        shopId: $0.shopId,
                        role: $0.role,
                        deviceId: $0.deviceId,
                        deviceName: $0.deviceName,
                        createdAtUtc: $0.createdAtUtc,
                        expiresAtUtc: $0.expiresAtUtc,
                        lastSeenAtUtc: $0.lastSeenAtUtc,
                        isRevoked: true
                    )
                    : $0
            }
            statusMessage = "Session revoked."
        }
    }

    func revokeAllSessions() async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            try await api.requestVoid("api/v1/account/sessions/revoke-all", accessToken: accessToken)
            sessions = sessions.map {
                SessionView(
                    sessionId: $0.sessionId,
                    shopId: $0.shopId,
                    role: $0.role,
                    deviceId: $0.deviceId,
                    deviceName: $0.deviceName,
                    createdAtUtc: $0.createdAtUtc,
                    expiresAtUtc: $0.expiresAtUtc,
                    lastSeenAtUtc: $0.lastSeenAtUtc,
                    isRevoked: true
                )
            }
            statusMessage = "All sessions revoked."
        }
    }

    func createSale(
        customerName: String,
        customerPhone: String,
        applyShopDiscount: Bool,
        dueDate: Date?,
        lines: [SaleLineDraft],
        payments: [SalePaymentRequest]
    ) async -> ReceiptView? {
        guard let accessToken = auth?.accessToken, let shop = currentShop else { return nil }
        guard !lines.isEmpty else {
            statusMessage = "Add at least one item to the sale."
            return nil
        }

        var createdReceipt: ReceiptView?
        await perform { [self] in
            let subtotal = lines.reduce(0) { $0 + $1.lineTotal }
            let discountAmount = applyShopDiscount ? subtotal * shop.defaultDiscountPercent : 0
            let response: CreateSaleResponse = try await api.request(
                "api/v1/sales",
                method: "POST",
                body: CreateSaleRequest(
                    customerName: nullable(customerName),
                    customerPhone: nullable(customerPhone),
                    discountAmount: discountAmount,
                    isCredit: dueDate != nil,
                    dueDateUtc: dueDate.map { backendDateTime($0) },
                    lines: lines.map {
                        SaleLineRequest(
                            inventoryItemId: $0.inventoryItemId,
                            quantity: $0.quantity,
                            unitPrice: $0.unitPrice
                        )
                    },
                    initialPayments: payments.isEmpty ? nil : payments
                ),
                accessToken: accessToken
            )
            let detail: SaleDetailResponse = try await api.request("api/v1/sales/\(response.id)", accessToken: accessToken)
            let receipt: ReceiptView = try await api.request("api/v1/sales/\(response.id)/receipt", accessToken: accessToken)
            recentSales.insert(detail, at: 0)
            lastReceipt = receipt
            createdReceipt = receipt
            await refreshInventory()
            await refreshCredits()
            await refreshTodaysSales()
            statusMessage = "Sale \(response.saleNumber) created."
        }
        return createdReceipt
    }

    func refreshTodaysSales() async {
        guard let accessToken = auth?.accessToken, capabilities.canManageSales else { return }
        await perform { [self] in
            todaysSalesReport = try await api.request(
                "api/v1/reports/sales?from=\(isoDay(Date()))&to=\(isoDay(Date()))",
                accessToken: accessToken
            )
        }
    }

    func refreshCredits() async {
        guard let accessToken = auth?.accessToken, capabilities.canManageSales else { return }
        await perform { [self] in
            credits = try await api.request("api/v1/credits", accessToken: accessToken)
        }
    }

    func loadCreditDetail(saleId: String) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            selectedCreditDetail = try await api.request("api/v1/credits/\(saleId)", accessToken: accessToken)
        }
    }

    func addRepayment(saleId: String, amount: Double, method: PaymentMethodOption, reference: String, notes: String) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            _ = try await api.requestData(
                "api/v1/credits/\(saleId)/repayments",
                method: "POST",
                body: CreditRepaymentRequest(
                    amount: amount,
                    method: method.rawValue,
                    reference: nullable(reference),
                    notes: nullable(notes)
                ),
                accessToken: accessToken
            )
            await refreshCredits()
            await loadCreditDetail(saleId: saleId)
            await refreshTodaysSales()
            statusMessage = "Repayment recorded."
        }
    }

    func fetchReportSummary(kind: ReportKind, from: Date? = nil, to: Date? = nil) async throws -> ReportSummary {
        guard let accessToken = auth?.accessToken else { throw APIError.message("Not signed in.") }
        let fromQuery = from.map { backendDate($0) }
        let toQuery = to.map { backendDate($0) }

        switch kind {
        case .inventory:
            let report: InventoryReportResponse = try await api.request("api/v1/reports/inventory", accessToken: accessToken)
            return ReportSummary(title: "Inventory", lines: [
                "Products: \(report.totalProducts)",
                "Units: \(report.totalUnits)",
                "Low stock: \(report.lowStockItems)",
                String(format: "Cost value: NGN %.2f", report.totalCostValue),
                String(format: "Selling value: NGN %.2f", report.totalSellingValue)
            ])
        case .sales:
            let report: SalesReportResponse = try await api.request("api/v1/reports/sales?\(dateRangeQuery(from: fromQuery, to: toQuery))", accessToken: accessToken)
            return ReportSummary(title: "Sales", lines: [
                "Sales count: \(report.salesCount)",
                String(format: "Revenue: NGN %.2f", report.revenue),
                String(format: "VAT: NGN %.2f", report.vatAmount),
                String(format: "Discounts: NGN %.2f", report.discountAmount),
                String(format: "Outstanding: NGN %.2f", report.outstandingAmount)
            ])
        case .profitLoss:
            let report: ProfitLossReportResponse = try await api.request("api/v1/reports/profit-loss?\(dateRangeQuery(from: fromQuery, to: toQuery))", accessToken: accessToken)
            return ReportSummary(title: "Profit & Loss", lines: [
                String(format: "Revenue: NGN %.2f", report.revenue),
                String(format: "COGS: NGN %.2f", report.cogs),
                String(format: "Expenses: NGN %.2f", report.expenses),
                String(format: "Net: NGN %.2f", report.netProfitLoss)
            ])
        case .creditors:
            let report: CreditorsReportResponse = try await api.request("api/v1/reports/creditors?\(dateRangeQuery(from: fromQuery, to: toQuery))", accessToken: accessToken)
            return ReportSummary(title: "Creditors", lines: [
                "Open credits: \(report.openCredits)",
                String(format: "Outstanding: NGN %.2f", report.totalOutstanding)
            ])
        }
    }

    func refreshReportArtifacts() async {
        guard let accessToken = auth?.accessToken, capabilities.canViewReports else { return }
        await perform { [self] in
            reportJobs = try await api.request("api/v1/reports/jobs", accessToken: accessToken)
            reportFiles = try await api.request("api/v1/reports/files", accessToken: accessToken)
        }
    }

    func queueReport(kind: ReportKind, format: ReportFormat, from: Date? = nil, to: Date? = nil) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let job: ReportJobView = try await api.request(
                "api/v1/reports/jobs",
                method: "POST",
                body: QueueReportJobRequest(
                    reportType: kind.apiName,
                    format: format.rawValue,
                    fromUtc: from.map { backendDayStart($0) },
                    toUtc: to.map { backendDayEnd($0) }
                ),
                accessToken: accessToken
            )
            reportJobs.insert(job, at: 0)
            statusMessage = "Report job queued."
        }
    }

    func retryReportJob(_ job: ReportJobView) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let retried: ReportJobView = try await api.request(
                "api/v1/reports/jobs/\(job.id)/retry",
                method: "POST",
                accessToken: accessToken
            )
            reportJobs = reportJobs.map { $0.id == retried.id ? retried : $0 }
            statusMessage = "Report job retried."
        }
    }

    func downloadReportFile(_ file: ReportFileView) async throws -> URL {
        guard let accessToken = auth?.accessToken else { throw APIError.message("Not signed in.") }
        let data = try await api.requestData("api/v1/reports/files/\(file.id)/download", accessToken: accessToken)
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("shopkeeper-ios-reports", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let fileURL = directory.appendingPathComponent(file.fileName)
        try data.write(to: fileURL, options: .atomic)
        return fileURL
    }

    func refreshExpenses() async {
        guard let accessToken = auth?.accessToken, capabilities.canManageExpenses else { return }
        await perform { [self] in
            expenses = try await api.request("api/v1/expenses", accessToken: accessToken)
        }
    }

    func createExpense(_ form: ExpenseFormState) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let created: ExpenseView = try await api.request(
                "api/v1/expenses",
                method: "POST",
                body: CreateExpenseRequest(
                    title: form.title.trimmingCharacters(in: .whitespacesAndNewlines),
                    category: form.category.trimmingCharacters(in: .whitespacesAndNewlines),
                    amount: Double(form.amount) ?? 0,
                    expenseDateUtc: backendDayStart(form.expenseDate),
                    notes: nullable(form.notes)
                ),
                accessToken: accessToken
            )
            expenses.insert(created, at: 0)
            statusMessage = "Expense created."
        }
    }

    func updateExpense(_ expense: ExpenseView, with form: ExpenseFormState) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let updated: ExpenseView = try await api.request(
                "api/v1/expenses/\(expense.id)",
                method: "PATCH",
                body: UpdateExpenseRequest(
                    title: form.title,
                    category: form.category,
                    amount: Double(form.amount) ?? expense.amount,
                    expenseDateUtc: backendDayStart(form.expenseDate),
                    notes: nullable(form.notes),
                    rowVersionBase64: expense.rowVersionBase64
                ),
                accessToken: accessToken
            )
            expenses = expenses.map { $0.id == updated.id ? updated : $0 }
            statusMessage = "Expense updated."
        }
    }

    func deleteExpense(_ expense: ExpenseView) async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            try await api.requestVoid("api/v1/expenses/\(expense.id)", method: "DELETE", accessToken: accessToken)
            expenses.removeAll { $0.id == expense.id }
            statusMessage = "Expense deleted."
        }
    }

    func runManualSync() async {
        guard let accessToken = auth?.accessToken else { return }
        await perform { [self] in
            let push: SyncPushResponse = try await api.request(
                "api/v1/sync/push",
                method: "POST",
                body: SyncPushRequest(changes: []),
                accessToken: accessToken
            )
            let pull: SyncPullResponse = try await api.request(
                "api/v1/sync/pull",
                method: "POST",
                body: SyncPullRequest(deviceId: deviceId(), sinceUtc: syncSummary.lastPulledAtUtc),
                accessToken: accessToken
            )
            syncSummary = SyncSummary(
                lastPulledAtUtc: pull.serverTimestampUtc,
                lastPushAccepted: push.acceptedCount,
                lastPullChanges: pull.changes.count,
                lastConflictCount: push.conflicts.count
            )
            syncConflicts = push.conflicts
            statusMessage = push.conflicts.isEmpty
                ? "Sync completed."
                : "Sync completed with \(push.conflicts.count) conflict(s)."
        }
    }

    func clearSyncConflicts() {
        syncConflicts = []
    }

    func resolveSyncConflictKeepServer(_ conflict: SyncConflictView) {
        if let payload = conflict.serverPayloadJson?.data(using: .utf8),
           let item = try? JSONDecoder().decode(InventoryItemResponse.self, from: payload),
           conflict.entityName == "InventoryItem"
        {
            if let existingIndex = inventory.firstIndex(where: { $0.id == item.id }) {
                inventory[existingIndex] = item
            } else {
                inventory.insert(item, at: 0)
            }
        } else if conflict.entityName == "InventoryItem" {
            inventory.removeAll { $0.id == conflict.entityId }
        }

        syncConflicts.removeAll { $0.id == conflict.id }
        syncSummary = SyncSummary(
            lastPulledAtUtc: syncSummary.lastPulledAtUtc,
            lastPushAccepted: syncSummary.lastPushAccepted,
            lastPullChanges: syncSummary.lastPullChanges,
            lastConflictCount: syncConflicts.count
        )
        statusMessage = "Conflict resolved with server values."
    }

    func resolveSyncConflictKeepLocal(_ conflict: SyncConflictView) {
        syncConflicts.removeAll { $0.id == conflict.id }
        syncSummary = SyncSummary(
            lastPulledAtUtc: syncSummary.lastPulledAtUtc,
            lastPushAccepted: syncSummary.lastPushAccepted,
            lastPullChanges: syncSummary.lastPullChanges,
            lastConflictCount: syncConflicts.count
        )
        statusMessage = "Conflict marked to keep local edits."
    }

    private func restoreSession() {
        guard
            let accessToken = defaults.string(forKey: "ios_access_token"),
            let refreshToken = defaults.string(forKey: "ios_refresh_token"),
            let shopId = defaults.string(forKey: "ios_shop_id"),
            let role = defaults.string(forKey: "ios_role")
        else {
            return
        }

        auth = AuthResponse(
            accessToken: accessToken,
            refreshToken: refreshToken,
            accessTokenExpiresAtUtc: "",
            shopId: shopId,
            role: role
        )
    }

    private func persistSession(_ response: AuthResponse) {
        defaults.set(response.accessToken, forKey: "ios_access_token")
        defaults.set(response.refreshToken, forKey: "ios_refresh_token")
        defaults.set(response.shopId, forKey: "ios_shop_id")
        defaults.set(response.role, forKey: "ios_role")
    }

    private func removeStoredSession() {
        defaults.removeObject(forKey: "ios_access_token")
        defaults.removeObject(forKey: "ios_refresh_token")
        defaults.removeObject(forKey: "ios_shop_id")
        defaults.removeObject(forKey: "ios_role")
    }

    private func restoreTheme() {
        if let raw = defaults.string(forKey: "ios_theme_preference"),
           let theme = AppThemePreference(rawValue: raw) {
            themePreference = theme
        }
    }

    private func perform(_ operation: @escaping () async throws -> Void) async {
        isBusy = true
        defer { isBusy = false }
        do {
            try await operation()
        } catch {
            statusMessage = error.localizedDescription
        }
    }
}

func initialsOf(_ fullName: String) -> String {
    let tokens = fullName
        .split(separator: " ")
        .prefix(2)
    let initials = tokens.compactMap { $0.first }.map(String.init).joined()
    return initials.isEmpty ? "SK" : initials.uppercased()
}

func isoDay(_ date: Date) -> String {
    let formatter = DateFormatter()
    formatter.calendar = Calendar(identifier: .gregorian)
    formatter.timeZone = TimeZone(secondsFromGMT: 0)
    formatter.dateFormat = "yyyy-MM-dd"
    return formatter.string(from: date)
}

func backendDate(_ date: Date) -> String {
    isoDay(date)
}

func backendDateTime(_ date: Date) -> String {
    let formatter = ISO8601DateFormatter()
    formatter.formatOptions = [.withInternetDateTime]
    formatter.timeZone = TimeZone(secondsFromGMT: 0)
    return formatter.string(from: date)
}

func backendDayStart(_ date: Date) -> String {
    let calendar = Calendar(identifier: .gregorian)
    let start = calendar.startOfDay(for: date)
    return backendDateTime(start)
}

func backendDayEnd(_ date: Date) -> String {
    let calendar = Calendar(identifier: .gregorian)
    let start = calendar.startOfDay(for: date)
    let end = calendar.date(byAdding: DateComponents(day: 1, second: -1), to: start) ?? start
    return backendDateTime(end)
}

func parseBackendDate(_ value: String?) -> Date? {
    guard let value, !value.isEmpty else { return nil }
    let formatter = DateFormatter()
    formatter.calendar = Calendar(identifier: .gregorian)
    formatter.timeZone = TimeZone(secondsFromGMT: 0)
    formatter.dateFormat = "yyyy-MM-dd"
    return formatter.date(from: value)
}

func parseBackendDateTime(_ value: String?) -> Date? {
    guard let value, !value.isEmpty else { return nil }
    return ISO8601DateFormatter().date(from: value)
}

func displayDate(_ value: String?) -> String {
    guard let date = parseBackendDateTime(value) ?? parseBackendDate(value) else {
        return value ?? "N/A"
    }

    let formatter = DateFormatter()
    formatter.dateStyle = .medium
    formatter.timeStyle = .short
    return formatter.string(from: date)
}

func decimalString(_ value: Double) -> String {
    String(format: "%.2f", value)
}

func nullable(_ value: String) -> String? {
    let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
    return trimmed.isEmpty ? nil : trimmed
}

func dateRangeQuery(from: String?, to: String?) -> String {
    var parts: [String] = []
    if let from, !from.isEmpty { parts.append("from=\(from)") }
    if let to, !to.isEmpty { parts.append("to=\(to)") }
    return parts.joined(separator: "&")
}

func deviceId() -> String {
    let defaults = UserDefaults.standard
    if let existing = defaults.string(forKey: "ios_device_id") {
        return existing
    }
    let value = UIDevice.current.identifierForVendor?.uuidString ?? UUID().uuidString
    defaults.set(value, forKey: "ios_device_id")
    return value
}

func saveImageToTemporaryLocation(_ image: UIImage) throws -> URL {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("shopkeeper-ios-images", isDirectory: true)
    try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let url = directory.appendingPathComponent("\(UUID().uuidString).jpg")
    guard let data = image.jpegData(compressionQuality: 0.88) else {
        throw APIError.message("Could not encode selected image.")
    }
    try data.write(to: url, options: .atomic)
    return url
}

func recognizeText(from image: UIImage) async throws -> String {
    guard let cgImage = image.cgImage else {
        throw APIError.message("Image format is not supported.")
    }

    return try await withCheckedThrowingContinuation { continuation in
        let request = VNRecognizeTextRequest { request, error in
            if let error {
                continuation.resume(throwing: error)
                return
            }

            let text = (request.results as? [VNRecognizedTextObservation])?
                .compactMap { $0.topCandidates(1).first?.string }
                .joined(separator: "\n") ?? ""
            continuation.resume(returning: text)
        }
        request.recognitionLevel = .accurate
        request.usesLanguageCorrection = true

        let handler = VNImageRequestHandler(cgImage: cgImage, options: [:])
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try handler.perform([request])
            } catch {
                continuation.resume(throwing: error)
            }
        }
    }
}

func extractInventoryCandidates(from text: String) -> (model: String, serial: String) {
    let lines = text
        .components(separatedBy: .newlines)
        .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
        .filter { !$0.isEmpty }

    let serial = lines.first(where: {
        let lower = $0.lowercased()
        return lower.contains("serial") || lower.contains("sn") || lower.range(of: #"[A-Z0-9\-]{6,}"#, options: .regularExpression) != nil
    }) ?? ""

    let model = lines.first(where: {
        let lower = $0.lowercased()
        return lower.contains("model") || lower.contains("mdl") || lower.range(of: #"[A-Z0-9\-]{4,}"#, options: .regularExpression) != nil
    }) ?? ""

    return (
        model.replacingOccurrences(of: "Model", with: "").replacingOccurrences(of: ":", with: "").trimmingCharacters(in: .whitespacesAndNewlines),
        serial.replacingOccurrences(of: "Serial", with: "").replacingOccurrences(of: ":", with: "").trimmingCharacters(in: .whitespacesAndNewlines)
    )
}

func extractReferenceCandidate(from text: String) -> String {
    let lines = text
        .components(separatedBy: .newlines)
        .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
        .filter { !$0.isEmpty }

    return lines.first(where: {
        $0.lowercased().contains("ref") ||
        $0.lowercased().contains("reference") ||
        $0.range(of: #"[A-Z0-9]{6,}"#, options: .regularExpression) != nil
    }) ?? lines.first ?? ""
}

func extractCustomerCandidate(from text: String) -> (name: String, phone: String) {
    let compact = text.replacingOccurrences(of: "\n", with: " ")
    let phoneMatch = compact.range(of: #"\+?\d[\d\s\-]{7,}"#, options: .regularExpression)
    let phone = phoneMatch.map { String(compact[$0]).trimmingCharacters(in: .whitespacesAndNewlines) } ?? ""
    let cleaned = compact.replacingOccurrences(of: phone, with: "").trimmingCharacters(in: .whitespacesAndNewlines)
    let tokens = cleaned.split(separator: " ").prefix(4)
    let name = tokens.joined(separator: " ")
    return (name, phone)
}

func generateReceiptPdf(_ receipt: ReceiptView) throws -> URL {
    let directory = FileManager.default.temporaryDirectory.appendingPathComponent("shopkeeper-ios-receipts", isDirectory: true)
    try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let url = directory.appendingPathComponent("\(receipt.saleNumber).pdf")

    let renderer = UIGraphicsPDFRenderer(bounds: CGRect(x: 0, y: 0, width: 595, height: 842))
    try renderer.writePDF(to: url) { context in
        context.beginPage()

        let titleAttributes: [NSAttributedString.Key: Any] = [
            .font: UIFont.boldSystemFont(ofSize: 22)
        ]
        let bodyAttributes: [NSAttributedString.Key: Any] = [
            .font: UIFont.systemFont(ofSize: 12)
        ]
        let boldBody: [NSAttributedString.Key: Any] = [
            .font: UIFont.boldSystemFont(ofSize: 12)
        ]

        var y: CGFloat = 32
        NSString(string: "Shopkeeper Receipt").draw(at: CGPoint(x: 32, y: y), withAttributes: titleAttributes)
        y += 32
        NSString(string: receipt.shopName).draw(at: CGPoint(x: 32, y: y), withAttributes: boldBody)
        y += 18
        NSString(string: "Sale No: \(receipt.saleNumber)").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "Date: \(displayDate(receipt.createdAtUtc))").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "Customer: \(receipt.customerName ?? "Walk-in Customer")").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 28

        for line in receipt.lines {
            NSString(string: "\(line.productName) x\(line.quantity)").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
            NSString(string: currency(line.lineTotal)).draw(at: CGPoint(x: 430, y: y), withAttributes: bodyAttributes)
            y += 18
        }

        y += 16
        NSString(string: "Subtotal: \(currency(receipt.subtotal))").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "VAT: \(currency(receipt.vatAmount))").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "Discount: \(currency(receipt.discountAmount))").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "Total: \(currency(receipt.totalAmount))").draw(at: CGPoint(x: 32, y: y), withAttributes: boldBody)
        y += 18
        NSString(string: "Paid: \(currency(receipt.paidAmount))").draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
        y += 18
        NSString(string: "Outstanding: \(currency(receipt.outstandingAmount))").draw(at: CGPoint(x: 32, y: y), withAttributes: boldBody)
        y += 28

        if !receipt.payments.isEmpty {
            NSString(string: "Payments").draw(at: CGPoint(x: 32, y: y), withAttributes: boldBody)
            y += 18
            for payment in receipt.payments {
                let line = "\(payment.paymentMethod.title): \(currency(payment.amount)) \(payment.reference ?? "")"
                NSString(string: line).draw(at: CGPoint(x: 32, y: y), withAttributes: bodyAttributes)
                y += 16
            }
        }
    }

    return url
}
