import SwiftUI
import UIKit

struct DashboardView: View {
    @EnvironmentObject private var sessionStore: SessionStore

    var body: some View {
        List {
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Text(sessionStore.currentShop?.name ?? "No shop selected")
                        .font(.title2.weight(.semibold))
                    Text(sessionStore.role.displayName)
                        .foregroundStyle(.secondary)
                }
            }

            Section("Overview") {
                MetricRow(title: "Today's Revenue", value: currency(sessionStore.dashboard.todaysRevenue), note: "\(sessionStore.dashboard.todaysSalesCount) sales")
                MetricRow(title: "Inventory Worth", value: currency(sessionStore.dashboard.totalInventoryWorth), note: "\(sessionStore.dashboard.totalInventoryUnits) units")
                MetricRow(title: "Open Credits", value: "\(sessionStore.dashboard.openCredits)", note: currency(sessionStore.dashboard.outstandingCredit))
                MetricRow(title: "Pending Reports", value: "\(sessionStore.dashboard.pendingReportJobs)", note: "\(sessionStore.dashboard.openConflicts) sync conflict(s)")
            }

            if let sales = sessionStore.todaysSalesReport {
                Section("Today's Sales Breakdown") {
                    MetricRow(title: "Revenue", value: currency(sales.revenue), note: "VAT \(currency(sales.vatAmount))")
                    MetricRow(title: "Discounts", value: currency(sales.discountAmount), note: "Outstanding \(currency(sales.outstandingAmount))")
                    if !sales.payments.isEmpty {
                        ForEach(sales.payments) { payment in
                            MetricRow(title: payment.method, value: currency(payment.amount), note: "Payment mix")
                        }
                    }
                }
            }

            Section("Actions") {
                Button("Refresh Dashboard") {
                    Task { await sessionStore.refreshAll() }
                }
                Button("Run Manual Sync") {
                    Task { await sessionStore.runManualSync() }
                }
            }
        }
        .navigationTitle("Dashboard")
        .task {
            if sessionStore.isAuthenticated {
                await sessionStore.refreshTodaysSales()
            }
        }
    }
}

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
        List {
            Section("Summary") {
                MetricRow(title: "Products", value: "\(sessionStore.dashboard.inventoryItems)", note: "\(sessionStore.dashboard.totalInventoryUnits) units")
                MetricRow(title: "Low Stock", value: "\(sessionStore.dashboard.lowStockItems)", note: currency(sessionStore.dashboard.totalInventoryWorth))
            }

            Section {
                TextField("Search inventory", text: $search)
            }

            Section("Products") {
                if filteredItems.isEmpty {
                    Text("No inventory items found.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(filteredItems) { item in
                        VStack(alignment: .leading, spacing: 6) {
                            Text(item.productName)
                                .font(.headline)
                            Text("Qty \(item.quantity) • Cost \(currency(item.costPrice)) • Sell \(currency(item.sellingPrice))")
                                .foregroundStyle(.secondary)
                            Text(detailLine(for: item))
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                            Button("Edit") {
                                editingItem = item
                                showingEditor = true
                            }
                            .buttonStyle(.bordered)
                        }
                        .padding(.vertical, 4)
                    }
                }
            }
        }
        .navigationTitle("Inventory")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    editingItem = nil
                    showingEditor = true
                } label: {
                    Image(systemName: "plus")
                }
            }
        }
        .sheet(isPresented: $showingEditor) {
            InventoryEditorView(item: editingItem)
                .environmentObject(sessionStore)
        }
        .refreshable {
            await sessionStore.refreshInventory()
        }
        .task {
            if sessionStore.inventory.isEmpty {
                await sessionStore.refreshInventory()
            }
        }
    }
}

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
            Form {
                Section("Camera") {
                    Text("Capture or import details, then review every field before saving.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    Menu("Scan Details") {
                        Button("Use Camera") {
                            pendingImageAction = .scanDetails
                            imageSource = .camera
                        }
                        Button("Import From Photos") {
                            pendingImageAction = .scanDetails
                            imageSource = .library
                        }
                    }
                    Menu("Add Item Photo") {
                        Button("Use Camera") {
                            pendingImageAction = .capturePhoto
                            imageSource = .camera
                        }
                        Button("Import From Photos") {
                            pendingImageAction = .capturePhoto
                            imageSource = .library
                        }
                    }
                    Text("Attached photos: \(form.photoUris.count)")
                        .foregroundStyle(.secondary)
                }

                Section("Product") {
                    TextField("Product name", text: $form.productName)
                    TextField("Model number", text: $form.modelNumber)
                    TextField("Serial number", text: $form.serialNumber)
                    Picker("Item type", selection: $form.itemType) {
                        ForEach(ItemTypeOption.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                }

                Section("Stock") {
                    TextField("Quantity", text: $form.quantity)
                        .keyboardType(.numberPad)
                    TextField("Cost price", text: $form.costPrice)
                        .keyboardType(.decimalPad)
                    TextField("Selling price", text: $form.sellingPrice)
                        .keyboardType(.decimalPad)
                    DatePicker(
                        "Expiry date",
                        selection: Binding(
                            get: { form.expiryDate ?? Date() },
                            set: { form.expiryDate = $0 }
                        ),
                        displayedComponents: .date
                    )
                }

                Section("Condition") {
                    Picker("Condition grade", selection: Binding(
                        get: { form.conditionGrade ?? .a },
                        set: { form.conditionGrade = $0 }
                    )) {
                        ForEach(ConditionGradeOption.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    TextField("Condition notes", text: $form.conditionNotes, axis: .vertical)
                        .lineLimit(4...8)
                }

                if let localStatus, !localStatus.isEmpty {
                    Section("Status") {
                        Text(localStatus)
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Actions") {
                    Button(item == nil ? "Create Item" : "Save Changes") {
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
                    }
                    .disabled(form.productName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)

                    if item != nil {
                        Button("Delete Item", role: .destructive) {
                            confirmingDelete = true
                        }
                    }
                }
            }
            .navigationTitle(item == nil ? "Add Item" : "Edit Item")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
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

struct SalesView: View {
    @EnvironmentObject private var sessionStore: SessionStore
    @State private var showingComposer = false
    @State private var sharedReceiptURL: URL?
    @State private var showingShareSheet = false

    var body: some View {
        List {
            if let report = sessionStore.todaysSalesReport {
                Section("Today") {
                    MetricRow(title: "Sales Count", value: "\(report.salesCount)", note: currency(report.revenue))
                    MetricRow(title: "VAT", value: currency(report.vatAmount), note: "Discounts \(currency(report.discountAmount))")
                    MetricRow(title: "Outstanding", value: currency(report.outstandingAmount), note: "Current day summary")
                }
            }

            Section("Recent Sales") {
                if sessionStore.recentSales.isEmpty {
                    Text("Newly created iPhone sales will appear here.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(sessionStore.recentSales) { sale in
                        VStack(alignment: .leading, spacing: 6) {
                            Text(sale.saleNumber)
                                .font(.headline)
                            Text("\(sale.customerName ?? "Walk-in Customer") • \(currency(sale.totalAmount))")
                            Text("VAT \(currency(sale.vatAmount)) • Discount \(currency(sale.discountAmount)) • Outstanding \(currency(sale.outstandingAmount))")
                                .foregroundStyle(.secondary)
                                .font(.footnote)
                        }
                        .padding(.vertical, 4)
                    }
                }
            }

            if let receipt = sessionStore.lastReceipt {
                Section("Last Receipt") {
                    Text(receipt.saleNumber)
                        .font(.headline)
                    Text("Shop: \(receipt.shopName)")
                    Text("Paid: \(currency(receipt.paidAmount)) • Outstanding: \(currency(receipt.outstandingAmount))")
                        .foregroundStyle(.secondary)
                    Button("Share Receipt PDF") {
                        do {
                            sharedReceiptURL = try generateReceiptPdf(receipt)
                            showingShareSheet = sharedReceiptURL != nil
                        } catch {
                            sessionStore.statusMessage = error.localizedDescription
                        }
                    }
                    .buttonStyle(.bordered)
                }
            }
        }
        .navigationTitle("Sales")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    showingComposer = true
                } label: {
                    Image(systemName: "plus")
                }
            }
        }
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
        .refreshable {
            await sessionStore.refreshTodaysSales()
            await sessionStore.refreshInventory()
        }
        .task {
            await sessionStore.refreshTodaysSales()
            if sessionStore.inventory.isEmpty {
                await sessionStore.refreshInventory()
            }
        }
    }
}

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

    private var filteredInventory: [InventoryItemResponse] {
        let candidates = sessionStore.inventory.filter { $0.quantity > 0 }
        guard !search.isEmpty else { return candidates }
        return candidates.filter {
            $0.productName.localizedCaseInsensitiveContains(search) ||
            ($0.modelNumber ?? "").localizedCaseInsensitiveContains(search) ||
            ($0.serialNumber ?? "").localizedCaseInsensitiveContains(search)
        }
    }

    private var subtotal: Double {
        lines.reduce(0) { $0 + $1.lineTotal }
    }

    private var discountAmount: Double {
        guard applyShopDiscount else { return 0 }
        return subtotal * (sessionStore.currentShop?.defaultDiscountPercent ?? 0)
    }

    private var vatAmount: Double {
        guard let shop = sessionStore.currentShop, shop.vatEnabled else { return 0 }
        return max(0, subtotal - discountAmount) * shop.vatRate
    }

    private var totalAmount: Double {
        max(0, subtotal - discountAmount) + vatAmount
    }

    private var paidAmount: Double {
        payments.reduce(0) { $0 + $1.amount }
    }

    private var outstandingAmount: Double {
        max(0, totalAmount - paidAmount)
    }

    private var saleDraft: SaleComposerDraft {
        SaleComposerDraft(
            customerName: customerName,
            customerPhone: customerPhone,
            search: search,
            lines: lines,
            selectedPaymentMethod: selectedPaymentMethod,
            paymentAmount: paymentAmount,
            paymentReference: paymentReference,
            payments: payments,
            applyShopDiscount: applyShopDiscount,
            isCredit: isCredit,
            dueDate: dueDate
        )
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Customer") {
                    TextField("Customer name", text: $customerName)
                    TextField("Customer phone", text: $customerPhone)
                        .keyboardType(.phonePad)
                    Menu("Scan Customer Details") {
                        Button("Use Camera") {
                            pendingScanAction = .customer
                            imageSource = .camera
                        }
                        Button("Import From Photos") {
                            pendingScanAction = .customer
                            imageSource = .library
                        }
                    }
                    Toggle("Apply shop discount", isOn: $applyShopDiscount)
                    Toggle("Sell on credit", isOn: $isCredit)
                    if isCredit {
                        DatePicker("Due date", selection: $dueDate, displayedComponents: .date)
                    }
                }

                Section("Add Items") {
                    TextField("Search inventory", text: $search)
                    ForEach(Array(filteredInventory.prefix(10))) { item in
                        VStack(alignment: .leading, spacing: 6) {
                            Text(item.productName)
                            Text("Qty \(item.quantity) • \(currency(item.sellingPrice))")
                                .foregroundStyle(.secondary)
                            Button("Add to sale") {
                                if let index = lines.firstIndex(where: { $0.inventoryItemId == item.id }) {
                                    lines[index].quantity += 1
                                } else {
                                    lines.append(
                                        SaleLineDraft(
                                            inventoryItemId: item.id,
                                            productName: item.productName,
                                            quantity: 1,
                                            unitPrice: item.sellingPrice
                                        )
                                    )
                                }
                            }
                            .buttonStyle(.bordered)
                        }
                    }
                }

                Section("Line Items") {
                    if lines.isEmpty {
                        Text("Add at least one product.")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach($lines) { $line in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(line.productName)
                                    .font(.headline)
                                Stepper("Quantity: \(line.quantity)", value: $line.quantity, in: 1...999)
                                TextField("Unit price", value: $line.unitPrice, format: .number)
                                    .keyboardType(.decimalPad)
                                Text("Line total: \(currency(line.lineTotal))")
                                    .foregroundStyle(.secondary)
                                Button("Remove", role: .destructive) {
                                    lines.removeAll { $0.id == line.id }
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    }
                }

                Section("Payments") {
                    Picker("Method", selection: $selectedPaymentMethod) {
                        ForEach(PaymentMethodOption.allCases) { method in
                            Text(method.title).tag(method)
                        }
                    }
                    TextField("Amount", text: $paymentAmount)
                        .keyboardType(.decimalPad)
                    TextField("Reference", text: $paymentReference)
                    Menu("Scan Payment Reference") {
                        Button("Use Camera") {
                            pendingScanAction = .reference
                            imageSource = .camera
                        }
                        Button("Import From Photos") {
                            pendingScanAction = .reference
                            imageSource = .library
                        }
                    }
                    Button("Add Payment") {
                        let amount = Double(paymentAmount) ?? 0
                        guard amount > 0 else { return }
                        payments.append(
                            SalePaymentRequest(
                                method: selectedPaymentMethod.rawValue,
                                amount: amount,
                                reference: nullable(paymentReference)
                            )
                        )
                        paymentAmount = ""
                        paymentReference = ""
                        selectedPaymentMethod = .cash
                    }
                    if payments.isEmpty {
                        Text("No initial payments added.")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(payments) { payment in
                            HStack {
                                Text(payment.paymentMethod.title)
                                Spacer()
                                Text(currency(payment.amount))
                            }
                        }
                    }
                }

                if let localStatus, !localStatus.isEmpty {
                    Section("Status") {
                        Text(localStatus)
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Totals") {
                    MetricRow(title: "Subtotal", value: currency(subtotal), note: "\(lines.count) line(s)")
                    MetricRow(title: "Discount", value: currency(discountAmount), note: applyShopDiscount ? "Shop preset" : "No discount")
                    MetricRow(title: "VAT", value: currency(vatAmount), note: sessionStore.currentShop?.vatEnabled == true ? "From shop settings" : "Disabled")
                    MetricRow(title: "Total", value: currency(totalAmount), note: "Paid \(currency(paidAmount))")
                    MetricRow(title: "Outstanding", value: currency(outstandingAmount), note: isCredit || outstandingAmount > 0 ? "Credit balance" : "Fully paid")
                }

                Section("Submit") {
                    Button("Create Sale") {
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
                            }
                            dismiss()
                        }
                    }
                    .disabled(lines.isEmpty)
                }
            }
            .navigationTitle("New Sale")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
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
                                if !customer.name.isEmpty {
                                    customerName = customer.name
                                }
                                if !customer.phone.isEmpty {
                                    customerPhone = customer.phone
                                }
                                localStatus = "Customer details extracted. Review them before creating the sale."
                            case .reference:
                                let referenceCandidate = extractReferenceCandidate(from: text)
                                if !referenceCandidate.isEmpty {
                                    paymentReference = referenceCandidate
                                }
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
        List {
            Section("Open Credit Sales") {
                if openCredits.isEmpty {
                    Text("No unsettled credit sales.")
                        .foregroundStyle(.secondary)
                } else {
                    Picker("Credit sale", selection: $selectedSaleId) {
                        Text("Select a credit sale").tag("")
                        ForEach(openCredits) { credit in
                            Text("\(credit.saleId.prefix(8)) • \(currency(credit.outstandingAmount))").tag(credit.saleId)
                        }
                    }
                    .onChange(of: selectedSaleId) { newValue in
                        guard !newValue.isEmpty else { return }
                        Task { await sessionStore.loadCreditDetail(saleId: newValue) }
                    }
                }
            }

            if let detail = sessionStore.selectedCreditDetail {
                Section("Selected Credit") {
                    MetricRow(title: "Outstanding", value: currency(detail.account.outstandingAmount), note: detail.account.status)
                    Text("Due: \(displayDate(detail.account.dueDateUtc))")
                        .foregroundStyle(.secondary)
                }

                Section("Repayment") {
                    TextField("Amount", text: $amount)
                        .keyboardType(.decimalPad)
                    Picker("Method", selection: Binding(
                        get: { method },
                        set: { method = $0 }
                    )) {
                        ForEach(PaymentMethodOption.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    TextField("Reference", text: $reference)
                    Menu("Scan Reference") {
                        Button("Use Camera") {
                            imageSource = .camera
                        }
                        Button("Import From Photos") {
                            imageSource = .library
                        }
                    }
                    TextField("Notes", text: $notes, axis: .vertical)
                        .lineLimit(3...6)
                    Button("Record Repayment") {
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
                    }
                    .disabled((Double(amount) ?? 0) <= 0)
                }

                if let localStatus, !localStatus.isEmpty {
                    Section("Status") {
                        Text(localStatus)
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Repayments") {
                    if detail.repayments.isEmpty {
                        Text("No repayments recorded yet.")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(detail.repayments) { repayment in
                            VStack(alignment: .leading, spacing: 4) {
                                Text("\(repayment.paymentMethod.title) • \(currency(repayment.amount))")
                                Text(repayment.reference ?? "No reference")
                                    .foregroundStyle(.secondary)
                                if let notes = repayment.notes, !notes.isEmpty {
                                    Text(notes)
                                        .font(.footnote)
                                        .foregroundStyle(.secondary)
                                }
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("Credits")
        .refreshable {
            await sessionStore.refreshCredits()
        }
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
                        if !referenceCandidate.isEmpty {
                            reference = referenceCandidate
                        }
                        localStatus = "Reference extracted. Review it before saving the repayment."
                    } catch {
                        localStatus = error.localizedDescription
                    }
                }
            }
        }
    }
}

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
        List {
            Section("Report Filters") {
                Picker("Report", selection: $selectedKind) {
                    ForEach(availableKinds) { kind in
                        Text(kind.title).tag(kind)
                    }
                }
                DatePicker("From", selection: $fromDate, displayedComponents: .date)
                DatePicker("To", selection: $toDate, displayedComponents: .date)
                Picker("Export format", selection: $selectedFormat) {
                    ForEach(ReportFormat.allCases) { format in
                        Text(format.title).tag(format)
                    }
                }
                Button("Load Summary") {
                    Task {
                        summary = try? await sessionStore.fetchReportSummary(kind: selectedKind, from: fromDate, to: toDate)
                    }
                }
                Button("Queue Export") {
                    Task {
                        await sessionStore.queueReport(kind: selectedKind, format: selectedFormat, from: fromDate, to: toDate)
                        await sessionStore.refreshReportArtifacts()
                    }
                }
            }

            Section("Summary") {
                if let summary {
                    ForEach(summary.lines, id: \.self) { line in
                        Text(line)
                    }
                } else {
                    Text("Load a report preview.")
                        .foregroundStyle(.secondary)
                }
            }

            if sessionStore.capabilities.canManageExpenses {
                Section("Expenses") {
                    Button("Add Expense") {
                        editingExpense = nil
                        showingExpenseSheet = true
                    }
                    ForEach(sessionStore.expenses) { expense in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(expense.title)
                                .font(.headline)
                            Text("\(expense.category) • \(currency(expense.amount))")
                                .foregroundStyle(.secondary)
                            Text(displayDate(expense.expenseDateUtc))
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                            HStack {
                                Button("Edit") {
                                    editingExpense = expense
                                    showingExpenseSheet = true
                                }
                                .buttonStyle(.bordered)
                                Button("Delete", role: .destructive) {
                                    Task { await sessionStore.deleteExpense(expense) }
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    }
                }
            }

            Section("Report Jobs") {
                if sessionStore.reportJobs.isEmpty {
                    Text("No queued report jobs yet.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(sessionStore.reportJobs) { job in
                        VStack(alignment: .leading, spacing: 4) {
                            Text("\(job.reportType.capitalized) • \(job.format.uppercased())")
                                .font(.headline)
                            Text(job.status)
                                .foregroundStyle(.secondary)
                            if let reason = job.failureReason, !reason.isEmpty {
                                Text(reason)
                                    .font(.footnote)
                                    .foregroundStyle(.secondary)
                            }
                            if job.status.lowercased().contains("failed") {
                                Button("Retry") {
                                    Task { await sessionStore.retryReportJob(job) }
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                    }
                }
            }

            Section("Files") {
                if sessionStore.reportFiles.isEmpty {
                    Text("No generated files available.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(sessionStore.reportFiles) { file in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(file.fileName)
                                .font(.headline)
                            Text("\(file.reportType.capitalized) • \(file.format.uppercased()) • \(displayDate(file.createdAtUtc))")
                                .foregroundStyle(.secondary)
                            Button("Share") {
                                Task {
                                    sharedFileURL = try? await sessionStore.downloadReportFile(file)
                                    showingShareSheet = sharedFileURL != nil
                                }
                            }
                            .buttonStyle(.bordered)
                        }
                    }
                }
            }
        }
        .navigationTitle("Reports")
        .sheet(isPresented: $showingShareSheet, onDismiss: {
            sharedFileURL = nil
        }) {
            if let url = sharedFileURL {
                ShareSheet(url: url, title: "Share Report")
            }
        }
        .sheet(isPresented: $showingExpenseSheet) {
            ExpenseEditorView(expense: editingExpense)
                .environmentObject(sessionStore)
        }
        .refreshable {
            await sessionStore.refreshReportArtifacts()
            await sessionStore.refreshExpenses()
        }
        .task {
            await sessionStore.refreshReportArtifacts()
            if sessionStore.capabilities.canManageExpenses {
                await sessionStore.refreshExpenses()
            }
        }
    }
}

struct ExpenseEditorView: View {
    @Environment(\.dismiss) private var dismiss
    @EnvironmentObject private var sessionStore: SessionStore

    let expense: ExpenseView?
    @State private var form = ExpenseFormState()

    var body: some View {
        NavigationStack {
            Form {
                Section("Expense") {
                    TextField("Title", text: $form.title)
                    TextField("Category", text: $form.category)
                    TextField("Amount", text: $form.amount)
                        .keyboardType(.decimalPad)
                    DatePicker("Expense date", selection: $form.expenseDate, displayedComponents: .date)
                    TextField("Notes", text: $form.notes, axis: .vertical)
                        .lineLimit(3...6)
                }

                Section("Actions") {
                    Button(expense == nil ? "Create Expense" : "Save Expense") {
                        Task {
                            if let expense {
                                await sessionStore.updateExpense(expense, with: form)
                            } else {
                                await sessionStore.createExpense(form)
                            }
                            dismiss()
                        }
                    }
                    .disabled(form.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .navigationTitle(expense == nil ? "Add Expense" : "Edit Expense")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Close") { dismiss() }
                }
            }
            .onAppear {
                form = expense.map(ExpenseFormState.init) ?? ExpenseFormState()
            }
        }
    }
}

struct SyncView: View {
    @EnvironmentObject private var sessionStore: SessionStore

    var body: some View {
        List {
            Section("Sync Status") {
                MetricRow(title: "Last Pull", value: displayDate(sessionStore.syncSummary.lastPulledAtUtc), note: "\(sessionStore.syncSummary.lastPullChanges) pulled changes")
                MetricRow(title: "Accepted Pushes", value: "\(sessionStore.syncSummary.lastPushAccepted)", note: "\(sessionStore.syncSummary.lastConflictCount) conflict(s)")
            }

            Section("Actions") {
                Button("Run Sync Now") {
                    Task { await sessionStore.runManualSync() }
                }
            }

            Section("Conflicts") {
                if sessionStore.syncConflicts.isEmpty {
                    Text("No unresolved sync conflicts.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(sessionStore.syncConflicts) { conflict in
                        VStack(alignment: .leading, spacing: 8) {
                            Text("\(conflict.entityName) • \(conflict.reason)")
                                .font(.headline)
                            Text(conflict.entityId)
                                .font(.footnote)
                                .foregroundStyle(.secondary)
                            HStack {
                                Button("Use Server") {
                                    sessionStore.resolveSyncConflictKeepServer(conflict)
                                }
                                .buttonStyle(.bordered)
                                Button("Keep Local") {
                                    sessionStore.resolveSyncConflictKeepLocal(conflict)
                                }
                                .buttonStyle(.bordered)
                            }
                        }
                        .padding(.vertical, 4)
                    }
                    Button("Clear All Conflicts", role: .destructive) {
                        sessionStore.clearSyncConflicts()
                    }
                }
            }
        }
        .navigationTitle("Sync")
    }
}

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
        List {
            Section("Account") {
                if let profile = sessionStore.profile {
                    TextField("Full name", text: Binding(
                        get: { fullName.isEmpty ? profile.fullName : fullName },
                        set: { fullName = $0 }
                    ))
                    Text(profile.email ?? "No email")
                        .foregroundStyle(.secondary)
                    TextField("Phone", text: Binding(
                        get: { phone.isEmpty ? (profile.phone ?? "") : phone },
                        set: { phone = $0 }
                    ))
                    TextField("Avatar URL", text: Binding(
                        get: { avatarUrl.isEmpty ? (profile.avatarUrl ?? "") : avatarUrl },
                        set: { avatarUrl = $0 }
                    ))
                    Picker("Theme", selection: Binding(
                        get: { sessionStore.themePreference },
                        set: { sessionStore.setTheme($0) }
                    )) {
                        ForEach(AppThemePreference.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    Button("Save Profile") {
                        Task {
                            await sessionStore.updateProfile(
                                fullName: fullName.isEmpty ? profile.fullName : fullName,
                                phone: phone.isEmpty ? (profile.phone ?? "") : phone,
                                avatarUrl: avatarUrl.isEmpty ? (profile.avatarUrl ?? "") : avatarUrl
                            )
                        }
                    }
                }
            }

            if let shop = sessionStore.currentShop {
                Section("Current Shop") {
                    Text(shop.name)
                    Text("Role: \(shop.shopRole.displayName)")
                        .foregroundStyle(.secondary)

                    if sessionStore.capabilities.canManageShopSettings {
                        Toggle("VAT Enabled", isOn: $vatEnabled)
                            .onAppear {
                                vatEnabled = shop.vatEnabled
                                vatRate = decimalString(shop.vatRate * 100)
                                discountPercent = decimalString(shop.defaultDiscountPercent * 100)
                            }
                        TextField("VAT Rate (%)", text: $vatRate)
                            .keyboardType(.decimalPad)
                        TextField("Default Discount (%)", text: $discountPercent)
                            .keyboardType(.decimalPad)
                        Button("Save Shop Settings") {
                            Task {
                                await sessionStore.updateShopSettings(
                                    vatEnabled: vatEnabled,
                                    vatRate: (Double(vatRate) ?? 7.5) / 100,
                                    discountPercent: (Double(discountPercent) ?? 0) / 100
                                )
                            }
                        }
                    }
                }
            }

            if sessionStore.capabilities.canManageStaff {
                Section("Invite Staff") {
                    TextField("Full name", text: $inviteFullName)
                    TextField("Email", text: $inviteEmail)
                    TextField("Phone", text: $invitePhone)
                    SecureField("Temporary password", text: $invitePassword)
                    Picker("Role", selection: $inviteRole) {
                        Text("Shop Manager").tag(ShopRole.shopManager)
                        Text("Salesperson").tag(ShopRole.salesperson)
                    }
                    Button("Invite Staff") {
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
                    }
                }

                Section("Team") {
                    if sessionStore.staffMembers.isEmpty {
                        Text("No staff records yet.")
                            .foregroundStyle(.secondary)
                    } else {
                        ForEach(sessionStore.staffMembers) { member in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(member.fullName)
                                    .font(.headline)
                                Text("\(member.shopRole.displayName) • \(member.isActive ? "Active" : "Inactive")")
                                    .foregroundStyle(.secondary)
                                HStack {
                                    Button(member.shopRole == .shopManager ? "Make Salesperson" : "Make Manager") {
                                        Task {
                                            await sessionStore.updateStaff(
                                                member,
                                                role: member.shopRole == .shopManager ? .salesperson : .shopManager,
                                                isActive: member.isActive
                                            )
                                        }
                                    }
                                    .buttonStyle(.bordered)
                                    Button(member.isActive ? "Disable" : "Activate") {
                                        Task {
                                            await sessionStore.updateStaff(member, role: member.shopRole, isActive: !member.isActive)
                                        }
                                    }
                                    .buttonStyle(.bordered)
                                }
                            }
                        }
                    }
                }
            }

            Section("Sign-in Methods") {
                if sessionStore.linkedIdentities.isEmpty {
                    Text("No linked identities.")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(sessionStore.linkedIdentities) { identity in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(identity.provider.capitalized)
                            Text(identity.email ?? identity.providerSubject)
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }

            Section("Sessions") {
                ForEach(sessionStore.sessions) { session in
                    VStack(alignment: .leading, spacing: 4) {
                        Text(session.deviceName ?? session.deviceId ?? "Unknown device")
                        Text("\(session.role) • \(session.isRevoked ? "Revoked" : "Active")")
                            .foregroundStyle(.secondary)
                        Button("Revoke Session", role: .destructive) {
                            Task { await sessionStore.revokeSession(session) }
                        }
                        .buttonStyle(.bordered)
                    }
                }
                Button("Revoke All Sessions", role: .destructive) {
                    Task { await sessionStore.revokeAllSessions() }
                }
            }

            Section("Session") {
                Button("Refresh") {
                    Task { await sessionStore.refreshAll() }
                }
                Button("Log Out", role: .destructive) {
                    sessionStore.logout()
                }
            }
        }
        .navigationTitle("Profile")
        .task {
            if sessionStore.isAuthenticated && sessionStore.sessions.isEmpty {
                await sessionStore.refreshAll()
            }
        }
    }
}

struct ShareSheet: View {
    let url: URL
    let title: String

    var body: some View {
        NavigationStack {
            VStack(spacing: 20) {
                Text(url.lastPathComponent)
                    .font(.headline)
                ShareLink(item: url) {
                    Label("Share File", systemImage: "square.and.arrow.up")
                }
                .buttonStyle(.borderedProminent)
            }
            .padding()
            .navigationTitle(title)
        }
    }
}

private struct MetricRow: View {
    let title: String
    let value: String
    let note: String

    var body: some View {
        HStack {
            VStack(alignment: .leading, spacing: 4) {
                Text(title)
                Text(note)
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Text(value)
                .font(.headline)
        }
    }
}

private func detailLine(for item: InventoryItemResponse) -> String {
    [
        item.modelNumber.map { "Model \($0)" },
        item.serialNumber.map { "Serial \($0)" },
        item.expiryDate.map { "Expiry \($0)" }
    ]
    .compactMap { $0 }
    .joined(separator: " • ")
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
    guard let data = try? encoder.encode(value) else {
        return ""
    }
    return String(data: data, encoding: .utf8) ?? ""
}

private func decodeDraft<T: Decodable>(_ raw: String) -> T? {
    guard let data = raw.data(using: .utf8), !raw.isEmpty else {
        return nil
    }
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
        case .camera:
            return .camera
        case .library:
            return .photoLibrary
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
