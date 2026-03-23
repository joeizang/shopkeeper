import SwiftUI
import UIKit

// MARK: - Dashboard

struct DashboardView: View {
    @EnvironmentObject private var sessionStore: SessionStore

    var body: some View {
        ScreenColumn {
            ScreenHeader(
                title: sessionStore.currentShop?.name ?? "Dashboard",
                subtitle: sessionStore.role.displayName
            )

            // Hero revenue card
            AccentCard {
                VStack(alignment: .leading, spacing: SKSpacing.sm) {
                    Text("Today's Revenue")
                        .font(.skLabelSmall)
                        .foregroundStyle(Color.skOnSurfaceVariant)
                    Text(currency(sessionStore.dashboard.todaysRevenue))
                        .font(.skMoneyLarge)
                        .foregroundStyle(Color.skSecondary)
                    Text("\(sessionStore.dashboard.todaysSalesCount) sales today")
                        .font(.skBodySmall)
                        .foregroundStyle(Color.skOnSurfaceVariant)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Metrics grid (2x2)
            LazyVGrid(columns: [GridItem(.flexible(), spacing: SKSpacing.md), GridItem(.flexible())], spacing: SKSpacing.md) {
                SKMetricCard(
                    title: "Inventory Worth",
                    value: currency(sessionStore.dashboard.totalInventoryWorth),
                    note: "\(sessionStore.dashboard.totalInventoryUnits) units",
                    icon: "shippingbox"
                )
                .staggeredAppearance(index: 0)
                SKMetricCard(
                    title: "Open Credits",
                    value: "\(sessionStore.dashboard.openCredits)",
                    note: currency(sessionStore.dashboard.outstandingCredit),
                    icon: "creditcard"
                )
                .staggeredAppearance(index: 1)
                SKMetricCard(
                    title: "Pending Reports",
                    value: "\(sessionStore.dashboard.pendingReportJobs)",
                    note: "\(sessionStore.dashboard.openConflicts) conflicts",
                    icon: "chart.bar"
                )
                .staggeredAppearance(index: 2)
                SKMetricCard(
                    title: "Products",
                    value: "\(sessionStore.dashboard.inventoryItems)",
                    note: "\(sessionStore.dashboard.lowStockItems) low stock",
                    icon: "cube.box"
                )
                .staggeredAppearance(index: 3)
            }

            // Today's sales breakdown
            if let sales = sessionStore.todaysSalesReport {
                SectionTitle(title: "Sales Breakdown")
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SKMetricRow(title: "Revenue", value: currency(sales.revenue), note: "VAT \(currency(sales.vatAmount))")
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        SKMetricRow(title: "Discounts", value: currency(sales.discountAmount), note: "Outstanding \(currency(sales.outstandingAmount))")
                        if !sales.payments.isEmpty {
                            Divider().overlay(Color.skOutline.opacity(0.3))
                            ForEach(sales.payments) { payment in
                                SKMetricRow(title: payment.method, value: currency(payment.amount), note: "Payment mix")
                            }
                        }
                    }
                }
            }

            // Actions
            SectionTitle(title: "Actions")
            HStack(spacing: SKSpacing.md) {
                SoftButton(title: "Refresh", action: {
                    Task { await sessionStore.refreshAll() }
                }, fullWidth: true)
                SoftButton(title: "Sync", action: {
                    Task { await sessionStore.runManualSync() }
                }, fullWidth: true)
            }

            // Bottom padding for floating tab bar
            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .accessibilityIdentifier("dashboard.root")
        .task {
            if sessionStore.isAuthenticated {
                await sessionStore.refreshTodaysSales()
            }
        }
    }
}

// MARK: - Inventory

struct InventoryView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var search = ""
    @State private var showingEditor = false
    @State private var editingItem: InventoryItemResponse?

    private var filteredItems: [InventoryItemResponse] {
        guard !search.isEmpty else { return sessionStore.inventory }
        return sessionStore.inventory.filter {
            $0.productName.localizedCaseInsensitiveContains(search) ||
            ($0.modelNumber ?? "").localizedCaseInsensitiveContains(search) ||
            ($0.serialNumber ?? "").localizedCaseInsensitiveContains(search)
        }
    }

    var body: some View {
        ScreenColumn {
            HStack {
                ScreenHeader(title: "Inventory")
                Spacer()
                Button {
                    editingItem = nil
                    showingEditor = true
                } label: {
                    Image(systemName: "plus.circle.fill")
                        .font(.system(size: 28))
                        .foregroundStyle(Color.skPrimary)
                }
                .accessibilityIdentifier("inventory.summary.add")
            }

            // Summary
            LazyVGrid(columns: [GridItem(.flexible(), spacing: SKSpacing.md), GridItem(.flexible())], spacing: SKSpacing.md) {
                SKMetricCard(
                    title: "Products",
                    value: "\(sessionStore.dashboard.inventoryItems)",
                    note: "\(sessionStore.dashboard.totalInventoryUnits) units"
                )
                SKMetricCard(
                    title: "Stock Worth",
                    value: currency(sessionStore.dashboard.totalInventoryWorth),
                    note: "\(sessionStore.dashboard.lowStockItems) low stock"
                )
            }

            // Search
            SKTextField(label: "Search", text: $search, accessibilityId: "inventory.summary.search")

            // Product list
            SectionTitle(title: "Products", subtitle: "\(filteredItems.count) items")

            if filteredItems.isEmpty {
                AccentCard {
                    VStack(spacing: SKSpacing.sm) {
                        Image(systemName: "shippingbox")
                            .font(.system(size: 32))
                            .foregroundStyle(Color.skOnSurfaceVariant.opacity(0.5))
                        Text("No inventory items found.")
                            .font(.skBodyMedium)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, SKSpacing.xl)
                }
            } else {
                ForEach(Array(filteredItems.enumerated()), id: \.element.id) { index, item in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text(item.productName)
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text("Qty \(item.quantity) \u{2022} Cost \(currency(item.costPrice)) \u{2022} Sell \(currency(item.sellingPrice))")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skSecondary)
                            Text(detailLine(for: item))
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            SoftButton(title: "Edit") {
                                editingItem = item
                                showingEditor = true
                            }
                        }
                    }
                    .staggeredAppearance(index: index)
                }
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .sheet(isPresented: $showingEditor) {
            InventoryEditorView(item: editingItem)
                .environmentObject(sessionStore)
        }
        .task {
            if sessionStore.inventory.isEmpty {
                await sessionStore.refreshInventory()
            }
        }
    }
}

// MARK: - Inventory Editor

struct InventoryEditorView: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var sessionStore: SessionStore

    let item: InventoryItemResponse?
    @AppStorage("ios_inventory_draft") private var inventoryDraftData = ""
    @State private var form = InventoryFormState()
    @State private var imageSource: ImageSourceOption?
    @State private var pendingImageAction: InventoryImageAction = .scanDetails
    @State private var localStatus: String?
    @State private var confirmingDelete = false

    var body: some View {
        NavigationStack {
            ScreenColumn {
                ScreenHeader(
                    title: item == nil ? "Add Item" : "Edit Item",
                    subtitle: "Fill in the product details below"
                )

                // Camera section
                AccentCard {
                    VStack(alignment: .leading, spacing: SKSpacing.md) {
                        SectionTitle(title: "Camera", subtitle: "Capture or import details, then review every field.")

                        HStack(spacing: SKSpacing.md) {
                            Menu {
                                Button("Use Camera") {
                                    pendingImageAction = .scanDetails
                                    imageSource = .camera
                                }
                                Button("Import From Photos") {
                                    pendingImageAction = .scanDetails
                                    imageSource = .library
                                }
                            } label: {
                                Label("Scan Details", systemImage: "doc.text.viewfinder")
                                    .font(.skLabelLarge)
                                    .foregroundStyle(Color.skOnSurface)
                                    .padding(SKSpacing.md)
                                    .background(
                                        RoundedRectangle(cornerRadius: SKShape.small)
                                            .stroke(Color.skOutline, lineWidth: 1)
                                    )
                            }

                            Menu {
                                Button("Use Camera") {
                                    pendingImageAction = .capturePhoto
                                    imageSource = .camera
                                }
                                Button("Import From Photos") {
                                    pendingImageAction = .capturePhoto
                                    imageSource = .library
                                }
                            } label: {
                                Label("Add Photo", systemImage: "camera")
                                    .font(.skLabelLarge)
                                    .foregroundStyle(Color.skOnSurface)
                                    .padding(SKSpacing.md)
                                    .background(
                                        RoundedRectangle(cornerRadius: SKShape.small)
                                            .stroke(Color.skOutline, lineWidth: 1)
                                    )
                            }
                        }

                        if form.photoUris.count > 0 {
                            Text("\(form.photoUris.count) photo(s) attached")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skTertiary)
                        }
                    }
                }

                // Product details
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Product")
                        SKTextField(label: "Product name", text: $form.productName, accessibilityId: "inventory.form.productName")
                        SKTextField(label: "Model number", text: $form.modelNumber)
                        SKTextField(label: "Serial number", text: $form.serialNumber)
                        HStack {
                            Text("Item type")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: $form.itemType) {
                                ForEach(ItemTypeOption.allCases) { option in
                                    Text(option.title).tag(option)
                                }
                            }
                            .tint(.skPrimary)
                        }
                    }
                }

                // Stock
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Stock")
                        SKTextField(label: "Quantity", text: $form.quantity, isDecimal: true, accessibilityId: "inventory.form.quantity")
                        SKTextField(label: "Cost price", text: $form.costPrice, isDecimal: true, accessibilityId: "inventory.form.costPrice")
                        SKTextField(label: "Selling price", text: $form.sellingPrice, isDecimal: true, accessibilityId: "inventory.form.sellingPrice")
                        HStack {
                            Text("Expiry date")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            DatePicker(
                                "",
                                selection: Binding(
                                    get: { form.expiryDate ?? Date() },
                                    set: { form.expiryDate = $0 }
                                ),
                                displayedComponents: .date
                            )
                            .tint(.skPrimary)
                        }
                    }
                }

                // Condition
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Condition")
                        HStack {
                            Text("Grade")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: Binding(
                                get: { form.conditionGrade ?? .a },
                                set: { form.conditionGrade = $0 }
                            )) {
                                ForEach(ConditionGradeOption.allCases) { option in
                                    Text(option.title).tag(option)
                                }
                            }
                            .tint(.skPrimary)
                        }
                        SKTextField(label: "Condition notes", text: $form.conditionNotes, isMultiLine: true)
                    }
                }

                // Status
                if let localStatus, !localStatus.isEmpty {
                    StatusBanner(message: localStatus, kind: .info)
                }

                // Actions
                BrickButton(
                    title: item == nil ? "Create Item" : "Save Changes",
                    action: {
                        Task {
                            if let item {
                                let existingUris = Set(item.photoUris)
                                let updated = await sessionStore.updateInventoryItem(item: item, form: form)
                                let targetItem = updated ?? item
                                for uri in form.photoUris where !existingUris.contains(uri) {
                                    await sessionStore.addInventoryPhoto(itemId: targetItem.id, photoUri: uri)
                                }
                            } else {
                                let created = await sessionStore.createInventoryItem(form)
                                if let created {
                                    for uri in form.photoUris {
                                        await sessionStore.addInventoryPhoto(itemId: created.id, photoUri: uri)
                                    }
                                    inventoryDraftData = ""
                                }
                            }
                            dismiss()
                        }
                    },
                    isDisabled: form.productName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                    accessibilityId: "inventory.form.save"
                )

                if item != nil {
                    SoftButton(title: "Delete Item", action: {
                        confirmingDelete = true
                    }, role: .destructive, fullWidth: true)
                }
            }
            .accessibilityIdentifier("inventory.editor.root")
            .navigationTitle(item == nil ? "Add Item" : "Edit Item")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .foregroundStyle(Color.skOnSurfaceVariant)
                }
            }
            .sheet(item: $imageSource) { source in
                ImagePicker(sourceType: source.uiKitSourceType) { image in
                    Task {
                        switch pendingImageAction {
                        case .scanDetails:
                            do {
                                let text = try await recognizeText(from: image)
                                let candidates = extractInventoryCandidates(from: text)
                                if !candidates.model.isEmpty {
                                    form.modelNumber = candidates.model
                                }
                                if !candidates.serial.isEmpty {
                                    form.serialNumber = candidates.serial
                                }
                                localStatus = "Scan complete. Review the extracted values before saving."
                            } catch {
                                localStatus = error.localizedDescription
                            }
                        case .capturePhoto:
                            do {
                                let url = try saveImageToTemporaryLocation(image)
                                form.photoUris.append(url.absoluteString)
                                localStatus = "Photo attached. It will upload when this item is saved."
                            } catch {
                                localStatus = error.localizedDescription
                            }
                        }
                    }
                }
            }
            .onAppear {
                if let item {
                    form = InventoryFormState(item: item)
                } else if let restored: InventoryFormState = decodeDraft(inventoryDraftData) {
                    form = restored
                } else {
                    form = InventoryFormState()
                }
            }
            .onChange(of: form) { newValue in
                guard item == nil else { return }
                inventoryDraftData = encodeDraft(newValue)
            }
            .alert("Delete Item", isPresented: $confirmingDelete) {
                Button("Delete", role: .destructive) {
                    guard let item else { return }
                    Task {
                        await sessionStore.deleteInventoryItem(item)
                        dismiss()
                    }
                }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("This removes the inventory item from the current shop.")
            }
        }
    }
}

// MARK: - Sales

struct SalesView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var showingComposer = false
    @State private var sharedReceiptURL: URL?
    @State private var showingShareSheet = false

    var body: some View {
        ScreenColumn {
            HStack {
                ScreenHeader(title: "Sales")
                Spacer()
                Button {
                    showingComposer = true
                } label: {
                    Image(systemName: "plus.circle.fill")
                        .font(.system(size: 28))
                        .foregroundStyle(Color.skPrimary)
                }
                .accessibilityIdentifier("sales.summary.add")
            }

            // Today's metrics
            if let report = sessionStore.todaysSalesReport {
                LazyVGrid(columns: [GridItem(.flexible(), spacing: SKSpacing.md), GridItem(.flexible())], spacing: SKSpacing.md) {
                    SKMetricCard(title: "Sales Count", value: "\(report.salesCount)", note: currency(report.revenue))
                    SKMetricCard(title: "VAT", value: currency(report.vatAmount), note: "Discounts \(currency(report.discountAmount))")
                }
                SKMetricCard(title: "Outstanding", value: currency(report.outstandingAmount), note: "Current day summary")
            }

            // Recent sales
            SectionTitle(title: "Recent Sales")

            if sessionStore.recentSales.isEmpty {
                AccentCard {
                    VStack(spacing: SKSpacing.sm) {
                        Image(systemName: "cart")
                            .font(.system(size: 32))
                            .foregroundStyle(Color.skOnSurfaceVariant.opacity(0.5))
                        Text("No recent sales.")
                            .font(.skBodyMedium)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, SKSpacing.xl)
                }
            } else {
                ForEach(sessionStore.recentSales) { sale in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text(sale.saleNumber)
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text("\(sale.customerName ?? "Walk-in Customer") \u{2022} \(currency(sale.totalAmount))")
                                .font(.skBodyMedium)
                                .foregroundStyle(Color.skSecondary)
                            Text("VAT \(currency(sale.vatAmount)) \u{2022} Discount \(currency(sale.discountAmount)) \u{2022} Outstanding \(currency(sale.outstandingAmount))")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                        }
                    }
                }
            }

            // Last receipt
            if let receipt = sessionStore.lastReceipt {
                SectionTitle(title: "Last Receipt")
                AccentCard {
                    VStack(alignment: .leading, spacing: SKSpacing.sm) {
                        Text(receipt.saleNumber)
                            .font(.skTitleMedium)
                            .foregroundStyle(Color.skOnSurface)
                        Text("Shop: \(receipt.shopName)")
                            .font(.skBodyMedium)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                        Text("Paid: \(currency(receipt.paidAmount)) \u{2022} Outstanding: \(currency(receipt.outstandingAmount))")
                            .font(.skBodySmall)
                            .foregroundStyle(Color.skSecondary)
                        SoftButton(title: "Share Receipt PDF") {
                            do {
                                sharedReceiptURL = try generateReceiptPdf(receipt)
                                showingShareSheet = sharedReceiptURL != nil
                            } catch {
                                sessionStore.statusMessage = error.localizedDescription
                            }
                        }
                    }
                }
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .sheet(isPresented: $showingComposer) {
            SaleComposerView()
                .environmentObject(sessionStore)
        }
        .sheet(isPresented: $showingShareSheet, onDismiss: {
            sharedReceiptURL = nil
        }) {
            if let url = sharedReceiptURL {
                ShareSheet(url: url, title: "Share Receipt")
            }
        }
        .task {
            await sessionStore.refreshTodaysSales()
            if sessionStore.inventory.isEmpty {
                await sessionStore.refreshInventory()
            }
        }
    }
}

// MARK: - Sale Composer

struct SaleComposerView: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var sessionStore: SessionStore

    @AppStorage("ios_sale_draft") private var saleDraftData = ""
    @State private var customerName = ""
    @State private var customerPhone = ""
    @State private var search = ""
    @State private var lines: [SaleLineDraft] = []
    @State private var selectedPaymentMethod: PaymentMethodOption = .cash
    @State private var paymentAmount = ""
    @State private var paymentReference = ""
    @State private var payments: [SalePaymentRequest] = []
    @State private var applyShopDiscount = false
    @State private var isCredit = false
    @State private var dueDate = Date().addingTimeInterval(60 * 60 * 24 * 30)
    @State private var imageSource: ImageSourceOption?
    @State private var pendingScanAction: SaleImageAction = .customer
    @State private var localStatus: String?
    @State private var showCelebration = false

    private var filteredInventory: [InventoryItemResponse] {
        let candidates = sessionStore.inventory.filter { $0.quantity > 0 }
        guard !search.isEmpty else { return candidates }
        return candidates.filter {
            $0.productName.localizedCaseInsensitiveContains(search) ||
            ($0.modelNumber ?? "").localizedCaseInsensitiveContains(search) ||
            ($0.serialNumber ?? "").localizedCaseInsensitiveContains(search)
        }
    }

    private var subtotal: Double { lines.reduce(0) { $0 + $1.lineTotal } }
    private var discountAmount: Double {
        guard applyShopDiscount else { return 0 }
        return subtotal * (sessionStore.currentShop?.defaultDiscountPercent ?? 0)
    }
    private var vatAmount: Double {
        guard let shop = sessionStore.currentShop, shop.vatEnabled else { return 0 }
        return max(0, subtotal - discountAmount) * shop.vatRate
    }
    private var totalAmount: Double { max(0, subtotal - discountAmount) + vatAmount }
    private var paidAmount: Double { payments.reduce(0) { $0 + $1.amount } }
    private var outstandingAmount: Double { max(0, totalAmount - paidAmount) }

    private var saleDraft: SaleComposerDraft {
        SaleComposerDraft(
            customerName: customerName, customerPhone: customerPhone,
            search: search, lines: lines,
            selectedPaymentMethod: selectedPaymentMethod,
            paymentAmount: paymentAmount, paymentReference: paymentReference,
            payments: payments, applyShopDiscount: applyShopDiscount,
            isCredit: isCredit, dueDate: dueDate
        )
    }

    var body: some View {
        NavigationStack {
            ScreenColumn {
                ScreenHeader(title: "New Sale", subtitle: "Add items and payment details")

                // Customer
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Customer")
                        SKTextField(label: "Customer name", text: $customerName, accessibilityId: "sales.form.customerName")
                        SKTextField(label: "Customer phone", text: $customerPhone, isPhone: true, accessibilityId: "sales.form.customerPhone")
                        Menu {
                            Button("Use Camera") { pendingScanAction = .customer; imageSource = .camera }
                            Button("Import From Photos") { pendingScanAction = .customer; imageSource = .library }
                        } label: {
                            Label("Scan Customer Details", systemImage: "doc.text.viewfinder")
                                .font(.skLabelLarge)
                                .foregroundStyle(Color.skOnSurface)
                                .padding(SKSpacing.md)
                                .frame(maxWidth: .infinity)
                                .background(
                                    RoundedRectangle(cornerRadius: SKShape.small)
                                        .stroke(Color.skOutline, lineWidth: 1)
                                )
                        }

                        HStack {
                            Toggle(isOn: $applyShopDiscount) {
                                Text("Apply shop discount")
                                    .font(.skBodyMedium)
                                    .foregroundStyle(Color.skOnSurface)
                            }
                            .tint(.skPrimary)
                        }
                        HStack {
                            Toggle(isOn: $isCredit) {
                                Text("Sell on credit")
                                    .font(.skBodyMedium)
                                    .foregroundStyle(Color.skOnSurface)
                            }
                            .tint(.skPrimary)
                        }
                        if isCredit {
                            HStack {
                                Text("Due date")
                                    .font(.skLabelSmall)
                                    .foregroundStyle(Color.skOnSurfaceVariant)
                                Spacer()
                                DatePicker("", selection: $dueDate, displayedComponents: .date)
                                    .tint(.skPrimary)
                            }
                        }
                    }
                }

                // Add items
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Add Items")
                        SKTextField(label: "Search inventory", text: $search, accessibilityId: "sales.form.searchInventory")
                        ForEach(Array(filteredInventory.prefix(10))) { item in
                            HStack {
                                VStack(alignment: .leading, spacing: SKSpacing.xs) {
                                    Text(item.productName)
                                        .font(.skBodyMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                    Text("Qty \(item.quantity) \u{2022} \(currency(item.sellingPrice))")
                                        .font(.skBodySmall)
                                        .foregroundStyle(Color.skOnSurfaceVariant)
                                }
                                Spacer()
                                Button {
                                    let generator = UIImpactFeedbackGenerator(style: .light)
                                    generator.impactOccurred()
                                    if let index = lines.firstIndex(where: { $0.inventoryItemId == item.id }) {
                                        lines[index].quantity += 1
                                    } else {
                                        lines.append(SaleLineDraft(
                                            inventoryItemId: item.id,
                                            productName: item.productName,
                                            quantity: 1,
                                            unitPrice: item.sellingPrice
                                        ))
                                    }
                                } label: {
                                    Image(systemName: "plus.circle.fill")
                                        .font(.system(size: 24))
                                        .foregroundStyle(Color.skPrimary)
                                }
                                .buttonStyle(.plain)
                                .accessibilityIdentifier("sales.item.add.\(item.productName)")
                            }
                            .padding(.vertical, SKSpacing.xs)
                            Divider().overlay(Color.skOutline.opacity(0.3))
                        }
                    }
                }

                // Line items
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Line Items")
                        if lines.isEmpty {
                            Text("Add at least one product.")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                        } else {
                            ForEach($lines) { $line in
                                VStack(alignment: .leading, spacing: SKSpacing.sm) {
                                    Text(line.productName)
                                        .font(.skTitleMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                    Stepper("Quantity: \(line.quantity)", value: $line.quantity, in: 1...999)
                                        .font(.skBodyMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                    HStack {
                                        Text("Unit price")
                                            .font(.skLabelSmall)
                                            .foregroundStyle(Color.skOnSurfaceVariant)
                                        Spacer()
                                        TextField("", value: $line.unitPrice, format: .number)
                                            .keyboardType(.decimalPad)
                                            .font(.skMoney)
                                            .foregroundStyle(Color.skSecondary)
                                            .multilineTextAlignment(.trailing)
                                            .frame(width: 120)
                                    }
                                    HStack {
                                        Text("Line total")
                                            .font(.skBodySmall)
                                            .foregroundStyle(Color.skOnSurfaceVariant)
                                        Spacer()
                                        Text(currency(line.lineTotal))
                                            .font(.skMoney)
                                            .foregroundStyle(Color.skSecondary)
                                    }
                                    Button(role: .destructive) {
                                        lines.removeAll { $0.id == line.id }
                                    } label: {
                                        Label("Remove", systemImage: "trash")
                                            .font(.skLabelSmall)
                                            .foregroundStyle(Color.skError)
                                    }
                                    .buttonStyle(.plain)
                                }
                                Divider().overlay(Color.skOutline.opacity(0.3))
                            }
                        }
                    }
                }

                // Payments
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Payments")
                        HStack {
                            Text("Method")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: $selectedPaymentMethod) {
                                ForEach(PaymentMethodOption.allCases) { method in
                                    Text(method.title).tag(method)
                                }
                            }
                            .tint(.skPrimary)
                        }
                        SKTextField(label: "Amount", text: $paymentAmount, isDecimal: true, accessibilityId: "sales.form.paymentAmount")
                        SKTextField(label: "Reference", text: $paymentReference, accessibilityId: "sales.form.paymentReference")
                        Menu {
                            Button("Use Camera") { pendingScanAction = .reference; imageSource = .camera }
                            Button("Import From Photos") { pendingScanAction = .reference; imageSource = .library }
                        } label: {
                            Label("Scan Reference", systemImage: "doc.text.viewfinder")
                                .font(.skLabelLarge)
                                .foregroundStyle(Color.skOnSurface)
                                .padding(SKSpacing.md)
                                .frame(maxWidth: .infinity)
                                .background(
                                    RoundedRectangle(cornerRadius: SKShape.small)
                                        .stroke(Color.skOutline, lineWidth: 1)
                                )
                        }
                        SoftButton(title: "Add Payment", action: {
                            let amount = Double(paymentAmount) ?? 0
                            guard amount > 0 else { return }
                            payments.append(SalePaymentRequest(
                                method: selectedPaymentMethod.rawValue,
                                amount: amount,
                                reference: nullable(paymentReference)
                            ))
                            paymentAmount = ""
                            paymentReference = ""
                            selectedPaymentMethod = .cash
                        }, fullWidth: true, accessibilityId: "sales.form.addPaymentSplit")

                        if !payments.isEmpty {
                            ForEach(payments) { payment in
                                HStack {
                                    Text(payment.paymentMethod.title)
                                        .font(.skBodyMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                    Spacer()
                                    Text(currency(payment.amount))
                                        .font(.skMoney)
                                        .foregroundStyle(Color.skSecondary)
                                }
                            }
                        }
                    }
                }

                // Status
                if let localStatus, !localStatus.isEmpty {
                    StatusBanner(message: localStatus, kind: .info)
                }

                // Totals
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Totals")
                        SKMetricRow(title: "Subtotal", value: currency(subtotal), note: "\(lines.count) line(s)")
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        SKMetricRow(title: "Discount", value: currency(discountAmount), note: applyShopDiscount ? "Shop preset" : "No discount")
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        SKMetricRow(title: "VAT", value: currency(vatAmount), note: sessionStore.currentShop?.vatEnabled == true ? "From shop settings" : "Disabled")
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        SKMetricRow(title: "Total", value: currency(totalAmount), note: "Paid \(currency(paidAmount))")
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        SKMetricRow(title: "Outstanding", value: currency(outstandingAmount), note: isCredit || outstandingAmount > 0 ? "Credit balance" : "Fully paid")
                    }
                }

                // Submit
                BrickButton(
                    title: "Create Sale",
                    action: {
                        Task {
                            let receipt = await sessionStore.createSale(
                                customerName: customerName,
                                customerPhone: customerPhone,
                                applyShopDiscount: applyShopDiscount,
                                dueDate: isCredit ? dueDate : nil,
                                lines: lines,
                                payments: payments
                            )
                            if receipt != nil {
                                saleDraftData = ""
                                let generator = UINotificationFeedbackGenerator()
                                generator.notificationOccurred(.success)
                                showCelebration = true
                            } else {
                                dismiss()
                            }
                        }
                    },
                    isDisabled: lines.isEmpty,
                    accessibilityId: "sales.form.save"
                )
            }
            .accessibilityIdentifier("sales.composer.root")
            .overlay {
                SaleCelebrationOverlay(isShowing: $showCelebration)
                    .onChange(of: showCelebration) { newValue in
                        if !newValue {
                            dismiss()
                        }
                    }
            }
            .navigationTitle("New Sale")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .foregroundStyle(Color.skOnSurfaceVariant)
                }
            }
            .onAppear {
                if let draft: SaleComposerDraft = decodeDraft(saleDraftData) {
                    customerName = draft.customerName
                    customerPhone = draft.customerPhone
                    search = draft.search
                    lines = draft.lines
                    selectedPaymentMethod = draft.selectedPaymentMethod
                    paymentAmount = draft.paymentAmount
                    paymentReference = draft.paymentReference
                    payments = draft.payments
                    applyShopDiscount = draft.applyShopDiscount
                    isCredit = draft.isCredit
                    dueDate = draft.dueDate
                }
            }
            .onChange(of: saleDraft) { newValue in
                saleDraftData = encodeDraft(newValue)
            }
            .sheet(item: $imageSource) { source in
                ImagePicker(sourceType: source.uiKitSourceType) { image in
                    Task {
                        do {
                            let text = try await recognizeText(from: image)
                            switch pendingScanAction {
                            case .customer:
                                let customer = extractCustomerCandidate(from: text)
                                if !customer.name.isEmpty { customerName = customer.name }
                                if !customer.phone.isEmpty { customerPhone = customer.phone }
                                localStatus = "Customer details extracted. Review them before creating the sale."
                            case .reference:
                                let referenceCandidate = extractReferenceCandidate(from: text)
                                if !referenceCandidate.isEmpty { paymentReference = referenceCandidate }
                                localStatus = "Payment reference extracted. Review it before adding the payment."
                            }
                        } catch {
                            localStatus = error.localizedDescription
                        }
                    }
                }
            }
        }
    }
}

// MARK: - Credits

struct CreditsView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @AppStorage("ios_credit_repayment_sale_id") private var selectedSaleId = ""
    @AppStorage("ios_credit_repayment_amount") private var amount = ""
    @AppStorage("ios_credit_repayment_method") private var methodRawValue = PaymentMethodOption.cash.rawValue
    @AppStorage("ios_credit_repayment_reference") private var reference = ""
    @AppStorage("ios_credit_repayment_notes") private var notes = ""
    @State private var imageSource: ImageSourceOption?
    @State private var localStatus: String?

    private var openCredits: [CreditAccountView] {
        sessionStore.credits.filter { $0.outstandingAmount > 0 && !$0.status.lowercased().contains("settled") }
    }

    private var method: PaymentMethodOption {
        get { PaymentMethodOption(rawValue: methodRawValue) ?? .cash }
        nonmutating set { methodRawValue = newValue.rawValue }
    }

    var body: some View {
        ScreenColumn {
            ScreenHeader(title: "Credits", subtitle: "Manage outstanding credit sales")

            // Credit selector
            AccentCard {
                VStack(spacing: SKSpacing.md) {
                    SectionTitle(title: "Open Credit Sales")
                    if openCredits.isEmpty {
                        Text("No unsettled credit sales.")
                            .font(.skBodyMedium)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                    } else {
                        Picker("Credit sale", selection: $selectedSaleId) {
                            Text("Select a credit sale").tag("")
                            ForEach(openCredits) { credit in
                                Text("\(credit.saleId.prefix(8)) \u{2022} \(currency(credit.outstandingAmount))").tag(credit.saleId)
                            }
                        }
                        .tint(.skPrimary)
                        .onChange(of: selectedSaleId) { newValue in
                            guard !newValue.isEmpty else { return }
                            Task { await sessionStore.loadCreditDetail(saleId: newValue) }
                        }
                        .accessibilityIdentifier("credits.selector")
                    }
                }
            }

            if let detail = sessionStore.selectedCreditDetail {
                // Selected credit info
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SKMetricRow(title: "Outstanding", value: currency(detail.account.outstandingAmount), note: detail.account.status)
                        Divider().overlay(Color.skOutline.opacity(0.3))
                        HStack {
                            Text("Due")
                                .font(.skBodyMedium)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Text(displayDate(detail.account.dueDateUtc))
                                .font(.skMoney)
                                .foregroundStyle(Color.skWarning)
                        }
                    }
                }

                // Repayment form
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Repayment")
                        SKTextField(label: "Amount", text: $amount, isDecimal: true, accessibilityId: "credits.repayment.amount")
                        HStack {
                            Text("Method")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: Binding(
                                get: { method },
                                set: { method = $0 }
                            )) {
                                ForEach(PaymentMethodOption.allCases) { option in
                                    Text(option.title).tag(option)
                                }
                            }
                            .tint(.skPrimary)
                        }
                        SKTextField(label: "Reference", text: $reference, accessibilityId: "credits.repayment.reference")
                        Menu {
                            Button("Use Camera") { imageSource = .camera }
                            Button("Import From Photos") { imageSource = .library }
                        } label: {
                            Label("Scan Reference", systemImage: "doc.text.viewfinder")
                                .font(.skLabelLarge)
                                .foregroundStyle(Color.skOnSurface)
                                .padding(SKSpacing.md)
                                .frame(maxWidth: .infinity)
                                .background(
                                    RoundedRectangle(cornerRadius: SKShape.small)
                                        .stroke(Color.skOutline, lineWidth: 1)
                                )
                        }
                        SKTextField(label: "Notes", text: $notes, isMultiLine: true, accessibilityId: "credits.repayment.notes")
                        BrickButton(
                            title: "Record Repayment",
                            action: {
                                Task {
                                    await sessionStore.addRepayment(
                                        saleId: detail.account.saleId,
                                        amount: Double(amount) ?? 0,
                                        method: method,
                                        reference: reference,
                                        notes: notes
                                    )
                                    amount = ""
                                }
                            },
                            isDisabled: (Double(amount) ?? 0) <= 0,
                            accessibilityId: "credits.repayment.submit"
                        )
                    }
                }
                .accessibilityIdentifier("credits.repayment.root")

                if let localStatus, !localStatus.isEmpty {
                    StatusBanner(message: localStatus, kind: .info)
                }

                // Repayment history
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Repayments")
                        if detail.repayments.isEmpty {
                            Text("No repayments recorded yet.")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                        } else {
                            ForEach(detail.repayments) { repayment in
                                VStack(alignment: .leading, spacing: SKSpacing.xs) {
                                    HStack {
                                        Text(repayment.paymentMethod.title)
                                            .font(.skBodyMedium)
                                            .foregroundStyle(Color.skOnSurface)
                                        Spacer()
                                        Text(currency(repayment.amount))
                                            .font(.skMoney)
                                            .foregroundStyle(Color.skSecondary)
                                    }
                                    Text(repayment.reference ?? "No reference")
                                        .font(.skBodySmall)
                                        .foregroundStyle(Color.skOnSurfaceVariant)
                                    if let n = repayment.notes, !n.isEmpty {
                                        Text(n)
                                            .font(.skBodySmall)
                                            .foregroundStyle(Color.skOnSurfaceVariant)
                                    }
                                }
                                Divider().overlay(Color.skOutline.opacity(0.3))
                            }
                        }
                    }
                }
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .task {
            await sessionStore.refreshCredits()
            if selectedSaleId.isEmpty, let first = openCredits.first {
                selectedSaleId = first.saleId
                await sessionStore.loadCreditDetail(saleId: first.saleId)
                reference = sessionStore.selectedCreditDetail?.repayments.first?.reference ?? ""
                notes = sessionStore.selectedCreditDetail?.repayments.first?.notes ?? ""
            }
        }
        .onChange(of: sessionStore.selectedCreditDetail?.account.saleId) { _ in
            let latestRepayment = sessionStore.selectedCreditDetail?.repayments.first
            reference = latestRepayment?.reference ?? ""
            notes = latestRepayment?.notes ?? ""
            method = latestRepayment?.paymentMethod ?? .cash
        }
        .sheet(item: $imageSource) { source in
            ImagePicker(sourceType: source.uiKitSourceType) { image in
                Task {
                    do {
                        let text = try await recognizeText(from: image)
                        let referenceCandidate = extractReferenceCandidate(from: text)
                        if !referenceCandidate.isEmpty { reference = referenceCandidate }
                        localStatus = "Reference extracted. Review it before saving the repayment."
                    } catch {
                        localStatus = error.localizedDescription
                    }
                }
            }
        }
    }
}

// MARK: - Reports

struct ReportsView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var selectedKind: ReportKind = .inventory
    @State private var selectedFormat: ReportFormat = .pdf
    @State private var fromDate = Date()
    @State private var toDate = Date()
    @State private var summary: ReportSummary?
    @State private var sharedFileURL: URL?
    @State private var showingShareSheet = false
    @State private var editingExpense: ExpenseView?
    @State private var showingExpenseSheet = false

    private var availableKinds: [ReportKind] {
        sessionStore.capabilities.canViewProfitLoss ? ReportKind.allCases : ReportKind.allCases.filter { $0 != .profitLoss }
    }

    var body: some View {
        ScreenColumn {
            ScreenHeader(title: "Reports")

            // Report type pills
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: SKSpacing.sm) {
                    ForEach(availableKinds) { kind in
                        SelectionPill(
                            title: kind.title,
                            isSelected: selectedKind == kind
                        ) { selectedKind = kind }
                    }
                }
            }

            // Filters
            AccentCard {
                VStack(spacing: SKSpacing.md) {
                    SectionTitle(title: "Filters")
                    HStack {
                        Text("From")
                            .font(.skLabelSmall)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                        Spacer()
                        DatePicker("", selection: $fromDate, displayedComponents: .date)
                            .tint(.skPrimary)
                    }
                    HStack {
                        Text("To")
                            .font(.skLabelSmall)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                        Spacer()
                        DatePicker("", selection: $toDate, displayedComponents: .date)
                            .tint(.skPrimary)
                    }
                    HStack {
                        Text("Export format")
                            .font(.skLabelSmall)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                        Spacer()
                        Picker("", selection: $selectedFormat) {
                            ForEach(ReportFormat.allCases) { format in
                                Text(format.title).tag(format)
                            }
                        }
                        .tint(.skPrimary)
                    }
                    HStack(spacing: SKSpacing.md) {
                        SoftButton(title: "Load Summary", action: {
                            Task {
                                summary = try? await sessionStore.fetchReportSummary(kind: selectedKind, from: fromDate, to: toDate)
                            }
                        }, fullWidth: true, accessibilityId: "reports.load")
                        BrickButton(title: "Queue Export", action: {
                            Task {
                                await sessionStore.queueReport(kind: selectedKind, format: selectedFormat, from: fromDate, to: toDate)
                                await sessionStore.refreshReportArtifacts()
                            }
                        }, accessibilityId: "reports.queue.export")
                    }
                }
            }

            // Summary
            if let summary {
                AccentCard {
                    VStack(alignment: .leading, spacing: SKSpacing.sm) {
                        SectionTitle(title: "Summary")
                        ForEach(summary.lines, id: \.self) { line in
                            Text(line)
                                .font(.skBodyMedium)
                                .foregroundStyle(Color.skOnSurface)
                        }
                    }
                }
            }

            // Expenses
            if sessionStore.capabilities.canManageExpenses {
                HStack {
                    SectionTitle(title: "Expenses")
                    Spacer()
                    Button {
                        editingExpense = nil
                        showingExpenseSheet = true
                    } label: {
                        Image(systemName: "plus.circle.fill")
                            .font(.system(size: 24))
                            .foregroundStyle(Color.skPrimary)
                    }
                }

                ForEach(sessionStore.expenses) { expense in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text(expense.title)
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text("\(expense.category) \u{2022} \(currency(expense.amount))")
                                .font(.skBodyMedium)
                                .foregroundStyle(Color.skSecondary)
                            Text(displayDate(expense.expenseDateUtc))
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            HStack(spacing: SKSpacing.md) {
                                SoftButton(title: "Edit") {
                                    editingExpense = expense
                                    showingExpenseSheet = true
                                }
                                SoftButton(title: "Delete", action: {
                                    Task { await sessionStore.deleteExpense(expense) }
                                }, role: .destructive)
                            }
                        }
                    }
                }
            }

            // Report jobs
            SectionTitle(title: "Report Jobs")
            if sessionStore.reportJobs.isEmpty {
                AccentCard {
                    Text("No queued report jobs yet.")
                        .font(.skBodySmall)
                        .foregroundStyle(Color.skOnSurfaceVariant)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, SKSpacing.md)
                }
            } else {
                ForEach(sessionStore.reportJobs) { job in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text("\(job.reportType.capitalized) \u{2022} \(job.format.uppercased())")
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text(job.status)
                                .font(.skBodySmall)
                                .foregroundStyle(
                                    job.status.lowercased().contains("fail") ? Color.skError
                                    : job.status.lowercased().contains("complete") ? Color.skSuccess
                                    : Color.skOnSurfaceVariant
                                )
                            if let reason = job.failureReason, !reason.isEmpty {
                                Text(reason)
                                    .font(.skBodySmall)
                                    .foregroundStyle(Color.skError)
                            }
                            if job.status.lowercased().contains("failed") {
                                SoftButton(title: "Retry") {
                                    Task { await sessionStore.retryReportJob(job) }
                                }
                            }
                        }
                    }
                }
            }

            // Files
            SectionTitle(title: "Files")
            if sessionStore.reportFiles.isEmpty {
                AccentCard {
                    Text("No generated files available.")
                        .font(.skBodySmall)
                        .foregroundStyle(Color.skOnSurfaceVariant)
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, SKSpacing.md)
                }
            } else {
                ForEach(sessionStore.reportFiles) { file in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text(file.fileName)
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text("\(file.reportType.capitalized) \u{2022} \(file.format.uppercased()) \u{2022} \(displayDate(file.createdAtUtc))")
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            SoftButton(title: "Share") {
                                Task {
                                    sharedFileURL = try? await sessionStore.downloadReportFile(file)
                                    showingShareSheet = sharedFileURL != nil
                                }
                            }
                        }
                    }
                }
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .sheet(isPresented: $showingShareSheet, onDismiss: { sharedFileURL = nil }) {
            if let url = sharedFileURL {
                ShareSheet(url: url, title: "Share Report")
            }
        }
        .sheet(isPresented: $showingExpenseSheet) {
            ExpenseEditorView(expense: editingExpense)
                .environmentObject(sessionStore)
        }
        .task {
            await sessionStore.refreshReportArtifacts()
            if sessionStore.capabilities.canManageExpenses {
                await sessionStore.refreshExpenses()
            }
        }
    }
}

// MARK: - Expense Editor

struct ExpenseEditorView: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var sessionStore: SessionStore

    let expense: ExpenseView?
    @State private var form = ExpenseFormState()

    var body: some View {
        NavigationStack {
            ScreenColumn {
                ScreenHeader(
                    title: expense == nil ? "Add Expense" : "Edit Expense",
                    subtitle: "Enter expense details"
                )

                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SKTextField(label: "Title", text: $form.title)
                        SKTextField(label: "Category", text: $form.category)
                        SKTextField(label: "Amount", text: $form.amount, isDecimal: true)
                        HStack {
                            Text("Expense date")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            DatePicker("", selection: $form.expenseDate, displayedComponents: .date)
                                .tint(.skPrimary)
                        }
                        SKTextField(label: "Notes", text: $form.notes, isMultiLine: true)
                    }
                }

                BrickButton(
                    title: expense == nil ? "Create Expense" : "Save Expense",
                    action: {
                        Task {
                            if let expense {
                                await sessionStore.updateExpense(expense, with: form)
                            } else {
                                await sessionStore.createExpense(form)
                            }
                            dismiss()
                        }
                    },
                    isDisabled: form.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                )
            }
            .navigationTitle(expense == nil ? "Add Expense" : "Edit Expense")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                        .foregroundStyle(Color.skOnSurfaceVariant)
                }
            }
            .onAppear {
                form = expense.map(ExpenseFormState.init) ?? ExpenseFormState()
            }
        }
    }
}

// MARK: - Sync

struct SyncView: View {
    @EnvironmentObject private var sessionStore: SessionStore

    var body: some View {
        ScreenColumn {
            ScreenHeader(title: "Sync", subtitle: "Manage data synchronization")

            // Status
            AccentCard {
                VStack(spacing: SKSpacing.md) {
                    SectionTitle(title: "Sync Status")
                    SKMetricRow(title: "Last Pull", value: displayDate(sessionStore.syncSummary.lastPulledAtUtc), note: "\(sessionStore.syncSummary.lastPullChanges) pulled changes")
                    Divider().overlay(Color.skOutline.opacity(0.3))
                    SKMetricRow(title: "Accepted Pushes", value: "\(sessionStore.syncSummary.lastPushAccepted)", note: "\(sessionStore.syncSummary.lastConflictCount) conflict(s)")
                }
            }

            BrickButton(title: "Run Sync Now", action: {
                Task { await sessionStore.runManualSync() }
            })

            // Conflicts
            SectionTitle(title: "Conflicts")
            if sessionStore.syncConflicts.isEmpty {
                AccentCard {
                    VStack(spacing: SKSpacing.sm) {
                        Image(systemName: "checkmark.circle")
                            .font(.system(size: 32))
                            .foregroundStyle(Color.skSuccess)
                        Text("No unresolved sync conflicts.")
                            .font(.skBodyMedium)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, SKSpacing.xl)
                }
            } else {
                ForEach(sessionStore.syncConflicts) { conflict in
                    AccentCard {
                        VStack(alignment: .leading, spacing: SKSpacing.sm) {
                            Text("\(conflict.entityName) \u{2022} \(conflict.reason)")
                                .font(.skTitleMedium)
                                .foregroundStyle(Color.skOnSurface)
                            Text(conflict.entityId)
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            HStack(spacing: SKSpacing.md) {
                                SoftButton(title: "Use Server") {
                                    sessionStore.resolveSyncConflictKeepServer(conflict)
                                }
                                SoftButton(title: "Keep Local") {
                                    sessionStore.resolveSyncConflictKeepLocal(conflict)
                                }
                            }
                        }
                    }
                }

                SoftButton(title: "Clear All Conflicts", action: {
                    sessionStore.clearSyncConflicts()
                }, role: .destructive, fullWidth: true)
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
    }
}

// MARK: - Profile

struct ProfileView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var fullName = ""
    @State private var phone = ""
    @State private var avatarUrl = ""
    @State private var vatEnabled = true
    @State private var vatRate = "7.5"
    @State private var discountPercent = "0"
    @State private var inviteFullName = ""
    @State private var inviteEmail = ""
    @State private var invitePhone = ""
    @State private var invitePassword = "Shopkeeper123!"
    @State private var inviteRole: ShopRole = .salesperson

    var body: some View {
        ScreenColumn {
            // Profile header with avatar
            HStack(spacing: SKSpacing.lg) {
                // Avatar circle
                ZStack {
                    Circle()
                        .fill(Color.skPrimary)
                        .frame(width: 56, height: 56)
                    Text(String((sessionStore.profile?.fullName ?? "U").prefix(2)).uppercased())
                        .font(.skTitleLarge)
                        .foregroundStyle(Color.skOnPrimary)
                }
                VStack(alignment: .leading, spacing: SKSpacing.xs) {
                    Text(sessionStore.profile?.fullName ?? "User")
                        .font(.skHeadlineMedium)
                        .foregroundStyle(Color.skOnBackground)
                    Text(sessionStore.profile?.email ?? "")
                        .font(.skBodySmall)
                        .foregroundStyle(Color.skOnSurfaceVariant)
                }
                Spacer()
            }

            // Account settings
            AccentCard {
                VStack(spacing: SKSpacing.md) {
                    SectionTitle(title: "Account")
                    if let profile = sessionStore.profile {
                        SKTextField(label: "Full name", text: Binding(
                            get: { fullName.isEmpty ? profile.fullName : fullName },
                            set: { fullName = $0 }
                        ))
                        SKTextField(label: "Phone", text: Binding(
                            get: { phone.isEmpty ? (profile.phone ?? "") : phone },
                            set: { phone = $0 }
                        ), isPhone: true)
                        SKTextField(label: "Avatar URL", text: Binding(
                            get: { avatarUrl.isEmpty ? (profile.avatarUrl ?? "") : avatarUrl },
                            set: { avatarUrl = $0 }
                        ))
                        HStack {
                            Text("Theme")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: Binding(
                                get: { sessionStore.themePreference },
                                set: { sessionStore.setTheme($0) }
                            )) {
                                ForEach(AppThemePreference.allCases) { option in
                                    Text(option.title).tag(option)
                                }
                            }
                            .tint(.skPrimary)
                        }
                        BrickButton(title: "Save Profile", action: {
                            Task {
                                await sessionStore.updateProfile(
                                    fullName: fullName.isEmpty ? profile.fullName : fullName,
                                    phone: phone.isEmpty ? (profile.phone ?? "") : phone,
                                    avatarUrl: avatarUrl.isEmpty ? (profile.avatarUrl ?? "") : avatarUrl
                                )
                            }
                        })
                    }
                }
            }

            // Shop settings
            if let shop = sessionStore.currentShop {
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Current Shop")
                        SKMetricRow(title: shop.name, value: shop.shopRole.displayName)

                        if sessionStore.capabilities.canManageShopSettings {
                            Divider().overlay(Color.skOutline.opacity(0.3))
                            HStack {
                                Toggle(isOn: $vatEnabled) {
                                    Text("VAT Enabled")
                                        .font(.skBodyMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                }
                                .tint(.skPrimary)
                            }
                            .onAppear {
                                vatEnabled = shop.vatEnabled
                                vatRate = decimalString(shop.vatRate * 100)
                                discountPercent = decimalString(shop.defaultDiscountPercent * 100)
                            }
                            SKTextField(label: "VAT Rate (%)", text: $vatRate, isDecimal: true)
                            SKTextField(label: "Default Discount (%)", text: $discountPercent, isDecimal: true)
                            BrickButton(title: "Save Shop Settings", action: {
                                Task {
                                    await sessionStore.updateShopSettings(
                                        vatEnabled: vatEnabled,
                                        vatRate: (Double(vatRate) ?? 7.5) / 100,
                                        discountPercent: (Double(discountPercent) ?? 0) / 100
                                    )
                                }
                            })
                        }
                    }
                }
            }

            // Staff management
            if sessionStore.capabilities.canManageStaff {
                AccentCard {
                    VStack(spacing: SKSpacing.md) {
                        SectionTitle(title: "Invite Staff")
                        SKTextField(label: "Full name", text: $inviteFullName)
                        SKTextField(label: "Email", text: $inviteEmail, isEmail: true)
                        SKTextField(label: "Phone", text: $invitePhone, isPhone: true)
                        SKSecureField(label: "Temporary password", text: $invitePassword)
                        HStack {
                            Text("Role")
                                .font(.skLabelSmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            Spacer()
                            Picker("", selection: $inviteRole) {
                                Text("Shop Manager").tag(ShopRole.shopManager)
                                Text("Salesperson").tag(ShopRole.salesperson)
                            }
                            .tint(.skPrimary)
                        }
                        BrickButton(title: "Invite Staff", action: {
                            Task {
                                await sessionStore.inviteStaff(
                                    fullName: inviteFullName,
                                    email: inviteEmail,
                                    phone: invitePhone,
                                    password: invitePassword,
                                    role: inviteRole
                                )
                                inviteFullName = ""
                                inviteEmail = ""
                                invitePhone = ""
                            }
                        })
                    }
                }

                // Team list
                SectionTitle(title: "Team")
                if sessionStore.staffMembers.isEmpty {
                    AccentCard {
                        Text("No staff records yet.")
                            .font(.skBodySmall)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, SKSpacing.md)
                    }
                } else {
                    ForEach(sessionStore.staffMembers) { member in
                        AccentCard {
                            VStack(alignment: .leading, spacing: SKSpacing.sm) {
                                Text(member.fullName)
                                    .font(.skTitleMedium)
                                    .foregroundStyle(Color.skOnSurface)
                                Text("\(member.shopRole.displayName) \u{2022} \(member.isActive ? "Active" : "Inactive")")
                                    .font(.skBodySmall)
                                    .foregroundStyle(member.isActive ? Color.skSuccess : Color.skOnSurfaceVariant)
                                HStack(spacing: SKSpacing.md) {
                                    SoftButton(title: member.shopRole == .shopManager ? "Make Salesperson" : "Make Manager") {
                                        Task {
                                            await sessionStore.updateStaff(
                                                member,
                                                role: member.shopRole == .shopManager ? .salesperson : .shopManager,
                                                isActive: member.isActive
                                            )
                                        }
                                    }
                                    SoftButton(title: member.isActive ? "Disable" : "Activate") {
                                        Task {
                                            await sessionStore.updateStaff(member, role: member.shopRole, isActive: !member.isActive)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Sign-in methods
            AccentCard {
                VStack(alignment: .leading, spacing: SKSpacing.md) {
                    SectionTitle(title: "Sign-in Methods")
                    if sessionStore.linkedIdentities.isEmpty {
                        Text("No linked identities.")
                            .font(.skBodySmall)
                            .foregroundStyle(Color.skOnSurfaceVariant)
                    } else {
                        ForEach(sessionStore.linkedIdentities) { identity in
                            HStack {
                                VStack(alignment: .leading, spacing: SKSpacing.xs) {
                                    Text(identity.provider.capitalized)
                                        .font(.skBodyMedium)
                                        .foregroundStyle(Color.skOnSurface)
                                    Text(identity.email ?? identity.providerSubject)
                                        .font(.skBodySmall)
                                        .foregroundStyle(Color.skOnSurfaceVariant)
                                }
                                Spacer()
                                Image(systemName: "checkmark.circle.fill")
                                    .foregroundStyle(Color.skSuccess)
                            }
                            Divider().overlay(Color.skOutline.opacity(0.3))
                        }
                    }
                }
            }

            // Sessions
            AccentCard {
                VStack(alignment: .leading, spacing: SKSpacing.md) {
                    SectionTitle(title: "Sessions")
                    ForEach(sessionStore.sessions) { session in
                        VStack(alignment: .leading, spacing: SKSpacing.xs) {
                            HStack {
                                Text(session.deviceName ?? session.deviceId ?? "Unknown device")
                                    .font(.skBodyMedium)
                                    .foregroundStyle(Color.skOnSurface)
                                Spacer()
                                Text(session.isRevoked ? "Revoked" : "Active")
                                    .font(.skLabelSmall)
                                    .foregroundStyle(session.isRevoked ? Color.skError : Color.skSuccess)
                            }
                            Text(session.role)
                                .font(.skBodySmall)
                                .foregroundStyle(Color.skOnSurfaceVariant)
                            if !session.isRevoked {
                                SoftButton(title: "Revoke", action: {
                                    Task { await sessionStore.revokeSession(session) }
                                }, role: .destructive)
                            }
                        }
                        Divider().overlay(Color.skOutline.opacity(0.3))
                    }
                    SoftButton(title: "Revoke All Sessions", action: {
                        Task { await sessionStore.revokeAllSessions() }
                    }, role: .destructive, fullWidth: true)
                }
            }

            // Session actions
            HStack(spacing: SKSpacing.md) {
                SoftButton(title: "Refresh", action: {
                    Task { await sessionStore.refreshAll() }
                }, fullWidth: true)
                SoftButton(title: "Log Out", action: {
                    sessionStore.logout()
                }, role: .destructive, fullWidth: true)
            }

            Spacer().frame(height: 80)
        }
        .navigationBarHidden(true)
        .task {
            if sessionStore.isAuthenticated && sessionStore.sessions.isEmpty {
                await sessionStore.refreshAll()
            }
        }
    }
}

// MARK: - Share Sheet

struct ShareSheet: View {
    let url: URL
    let title: String

    var body: some View {
        NavigationStack {
            VStack(spacing: SKSpacing.xl) {
                Image(systemName: "doc.fill")
                    .font(.system(size: 40))
                    .foregroundStyle(Color.skPrimary)
                Text(url.lastPathComponent)
                    .font(.skTitleMedium)
                    .foregroundStyle(Color.skOnBackground)
                ShareLink(item: url) {
                    Label("Share File", systemImage: "square.and.arrow.up")
                        .font(.skLabelLarge)
                        .foregroundStyle(Color.skOnPrimary)
                        .padding(.horizontal, SKSpacing.xl)
                        .padding(.vertical, SKSpacing.md)
                        .background(
                            RoundedRectangle(cornerRadius: SKShape.medium)
                                .fill(Color.skPrimary)
                        )
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background(ShopkeeperBackground())
            .navigationTitle(title)
        }
    }
}

// MARK: - Helpers

private func detailLine(for item: InventoryItemResponse) -> String {
    [
        item.modelNumber.map { "Model \($0)" },
        item.serialNumber.map { "Serial \($0)" },
        item.expiryDate.map { "Expiry \($0)" }
    ]
    .compactMap { $0 }
    .joined(separator: " \u{2022} ")
}

func currency(_ value: Double) -> String {
    let formatter = NumberFormatter()
    formatter.numberStyle = .currency
    formatter.currencyCode = "NGN"
    formatter.locale = Locale(identifier: "en_NG")
    return formatter.string(from: NSNumber(value: value)) ?? "NGN \(decimalString(value))"
}

private struct SaleComposerDraft: Codable, Equatable {
    let customerName: String
    let customerPhone: String
    let search: String
    let lines: [SaleLineDraft]
    let selectedPaymentMethod: PaymentMethodOption
    let paymentAmount: String
    let paymentReference: String
    let payments: [SalePaymentRequest]
    let applyShopDiscount: Bool
    let isCredit: Bool
    let dueDate: Date
}

private func encodeDraft<T: Encodable>(_ value: T) -> String {
    let encoder = JSONEncoder()
    encoder.dateEncodingStrategy = .iso8601
    guard let data = try? encoder.encode(value) else { return "" }
    return String(data: data, encoding: .utf8) ?? ""
}

private func decodeDraft<T: Decodable>(_ raw: String) -> T? {
    guard let data = raw.data(using: .utf8), !raw.isEmpty else { return nil }
    let decoder = JSONDecoder()
    decoder.dateDecodingStrategy = .iso8601
    return try? decoder.decode(T.self, from: data)
}

private enum ImageSourceOption: String, Identifiable {
    case camera
    case library

    var id: String { rawValue }

    var uiKitSourceType: UIImagePickerController.SourceType {
        switch self {
        case .camera: return .camera
        case .library: return .photoLibrary
        }
    }
}

private enum InventoryImageAction {
    case scanDetails
    case capturePhoto
}

private enum SaleImageAction {
    case customer
    case reference
}

private struct ImagePicker: UIViewControllerRepresentable {
    let sourceType: UIImagePickerController.SourceType
    let onImagePicked: (UIImage) -> Void
    @Environment(\.dismiss) private var dismiss

    func makeCoordinator() -> Coordinator {
        Coordinator(onImagePicked: onImagePicked, dismiss: dismiss.callAsFunction)
    }

    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController()
        picker.delegate = context.coordinator
        picker.allowsEditing = false
        picker.sourceType = UIImagePickerController.isSourceTypeAvailable(sourceType) ? sourceType : .photoLibrary
        return picker
    }

    func updateUIViewController(_ uiViewController: UIImagePickerController, context: Context) {}

    final class Coordinator: NSObject, UINavigationControllerDelegate, UIImagePickerControllerDelegate {
        let onImagePicked: (UIImage) -> Void
        let dismiss: () -> Void

        init(onImagePicked: @escaping (UIImage) -> Void, dismiss: @escaping () -> Void) {
            self.onImagePicked = onImagePicked
            self.dismiss = dismiss
        }

        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) {
            dismiss()
        }

        func imagePickerController(
            _ picker: UIImagePickerController,
            didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]
        ) {
            if let image = info[.originalImage] as? UIImage {
                onImagePicked(image)
            }
            dismiss()
        }
    }
}
