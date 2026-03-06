package com.shopkeeper.mobile.core.data

import android.content.Context
import android.provider.Settings
import com.shopkeeper.mobile.auth.AuthSessionManager
import com.shopkeeper.mobile.core.data.local.InventoryItemEntity
import com.shopkeeper.mobile.core.data.local.PendingItemPhotoEntity
import com.shopkeeper.mobile.core.data.local.SaleEntity
import com.shopkeeper.mobile.core.data.local.ShopkeeperDatabase
import com.shopkeeper.mobile.core.data.local.SyncConflictEntity
import com.shopkeeper.mobile.core.data.local.SyncQueueEntity
import com.shopkeeper.mobile.core.data.remote.AddItemPhotoRequest
import com.shopkeeper.mobile.core.data.remote.AccountProfileResponseDto
import com.shopkeeper.mobile.core.data.remote.CreditorsReportResponseDto
import com.shopkeeper.mobile.core.data.remote.CreateInventoryItemRequest
import com.shopkeeper.mobile.core.data.remote.CreateSaleRequest
import com.shopkeeper.mobile.core.data.remote.CreateSaleResponse
import com.shopkeeper.mobile.core.data.remote.CreditRepaymentRequest
import com.shopkeeper.mobile.core.data.remote.CreditDetailResponseDto
import com.shopkeeper.mobile.core.data.remote.CreditRepaymentViewDto
import com.shopkeeper.mobile.core.data.remote.GoogleMobileAuthRequest
import com.shopkeeper.mobile.core.data.remote.InventoryItemResponse
import com.shopkeeper.mobile.core.data.remote.InventoryReportResponseDto
import com.shopkeeper.mobile.core.data.remote.LoginRequest
import com.shopkeeper.mobile.core.data.remote.LinkedIdentityViewDto
import com.shopkeeper.mobile.core.data.remote.MagicLinkRequestDto
import com.shopkeeper.mobile.core.data.remote.MagicLinkRequestResponseDto
import com.shopkeeper.mobile.core.data.remote.MagicLinkVerifyRequestDto
import com.shopkeeper.mobile.core.data.remote.NetworkFactory
import com.shopkeeper.mobile.core.data.remote.ProfitLossReportResponseDto
import com.shopkeeper.mobile.core.data.remote.RegisterOwnerRequest
import com.shopkeeper.mobile.core.data.remote.SaleSyncPayload
import com.shopkeeper.mobile.core.data.remote.SalesReportResponseDto
import com.shopkeeper.mobile.core.data.remote.SessionViewDto
import com.shopkeeper.mobile.core.data.remote.SaleLineRequest
import com.shopkeeper.mobile.core.data.remote.SalePaymentRequest
import com.shopkeeper.mobile.core.data.remote.SyncPushChange
import com.shopkeeper.mobile.core.data.remote.SyncPullRequest
import com.shopkeeper.mobile.core.data.remote.SyncPushRequest
import com.shopkeeper.mobile.core.data.remote.UpdateAccountProfileRequestDto
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import retrofit2.HttpException
import java.io.File
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

class ShopkeeperDataGateway private constructor(private val appContext: Context) {
    private val db = ShopkeeperDatabase.get(appContext)
    private val sessionManager = AuthSessionManager(appContext)
    private val api = NetworkFactory.createApi(sessionManager)

    private val moshi = Moshi.Builder().addLast(KotlinJsonAdapterFactory()).build()
    private val inventoryCreateAdapter = moshi.adapter(CreateInventoryItemRequest::class.java)
    private val inventoryResponseAdapter = moshi.adapter(InventoryItemResponse::class.java)
    private val saleCreateAdapter = moshi.adapter(CreateSaleRequest::class.java)
    private val saleSyncPayloadAdapter = moshi.adapter(SaleSyncPayload::class.java)
    private val repaymentAdapter = moshi.adapter(CreditRepaymentEnvelope::class.java)
    private val syncPrefs = appContext.getSharedPreferences("shopkeeper_sync_meta", Context.MODE_PRIVATE)

    suspend fun refreshInventory(): List<InventoryItemEntity> = withContext(Dispatchers.IO) {
        val local = db.inventoryDao().getAll()

        try {
            val remoteItems = withAuthRetry { api.listInventory() }
            remoteItems.forEach { db.inventoryDao().upsert(it.toEntity()) }
            db.inventoryDao().getAll()
        } catch (_: Exception) {
            local
        }
    }

    suspend fun getLocalInventory(): List<InventoryItemEntity> = withContext(Dispatchers.IO) {
        db.inventoryDao().getAll()
    }

    suspend fun getLocalSales(): List<SaleEntity> = withContext(Dispatchers.IO) {
        db.salesDao().getAll()
    }

    suspend fun getTodayCompletedSales(): List<SaleEntity> = withContext(Dispatchers.IO) {
        val today = LocalDate.now()
        db.salesDao().getAll()
            .filter { it.status == "COMPLETED" }
            .filter { parseIsoDate(it.updatedAtUtcIso) == today }
            .sortedByDescending { it.updatedAtUtcIso }
    }

    suspend fun getOpenCreditSales(): List<CreditSaleOption> = withContext(Dispatchers.IO) {
        db.salesDao().getAll()
            .filter { it.isCredit && it.outstandingAmount > 0.0 }
            .sortedByDescending { it.updatedAtUtcIso }
            .map {
                CreditSaleOption(
                    saleId = it.id,
                    saleNumber = it.saleNumber,
                    customerName = it.customerName?.ifBlank { "Walk-in Customer" } ?: "Walk-in Customer",
                    itemSummary = it.lineItemsSummary.ifBlank { "Credit Sale" },
                    outstandingAmount = it.outstandingAmount
                )
            }
    }

    suspend fun getCreditDetail(saleId: String): Result<CreditDetail> = withContext(Dispatchers.IO) {
        try {
            Result.success(withAuthRetry { api.getCreditDetails(saleId).toModel() })
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    fun isOwnerSession(): Boolean {
        val role = sessionManager.role()
        if (role.isNullOrBlank()) {
            return true
        }
        return role.equals("Owner", ignoreCase = true)
    }

    suspend fun getAccountProfile(): Result<AccountProfile> = withContext(Dispatchers.IO) {
        try {
            Result.success(withAuthRetry { api.getAccountMe().toModel() })
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun updateAccountProfile(input: AccountProfileUpdateInput): Result<AccountProfile> = withContext(Dispatchers.IO) {
        try {
            val payload = UpdateAccountProfileRequestDto(
                fullName = input.fullName,
                phone = input.phone,
                avatarUrl = input.avatarUrl,
                preferredLanguage = input.preferredLanguage,
                timezone = input.timezone
            )
            Result.success(withAuthRetry { api.updateAccountMe(payload).toModel() })
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun getAccountSessions(): Result<List<AccountSession>> = withContext(Dispatchers.IO) {
        try {
            Result.success(withAuthRetry { api.getAccountSessions().map { it.toModel() } })
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun revokeAccountSession(sessionId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            withAuthRetry { api.revokeAccountSession(sessionId) }
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun getLinkedIdentities(): Result<List<LinkedIdentity>> = withContext(Dispatchers.IO) {
        try {
            Result.success(withAuthRetry { api.getLinkedIdentities().map { it.toModel() } })
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun requestMagicLink(email: String, shopId: String? = sessionManager.shopId()): Result<MagicLinkChallenge> = withContext(Dispatchers.IO) {
        try {
            val response = api.requestMagicLink(
                MagicLinkRequestDto(
                    email = email.trim(),
                    shopId = shopId
                )
            )
            Result.success(response.toModel())
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun verifyMagicLink(token: String, shopId: String? = sessionManager.shopId()): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val auth = api.verifyMagicLink(
                MagicLinkVerifyRequestDto(
                    token = token.trim(),
                    shopId = shopId
                )
            )
            sessionManager.saveSession(auth.accessToken, auth.refreshToken, auth.shopId, auth.role)
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun loginWithGoogleIdToken(idToken: String, shopId: String? = sessionManager.shopId()): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val auth = api.loginWithGoogle(
                GoogleMobileAuthRequest(
                    idToken = idToken,
                    shopId = shopId
                )
            )
            sessionManager.saveSession(auth.accessToken, auth.refreshToken, auth.shopId, auth.role)
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun getDashboardSummary(): DashboardSummary = withContext(Dispatchers.IO) {
        val today = LocalDate.now()
        val sales = db.salesDao().getAll()
        val inventory = db.inventoryDao().getAll()
        val conflicts = db.syncDao().getConflicts()

        val completedSales = sales.filter { it.status == "COMPLETED" }
        val todayCompleted = completedSales.filter { parseIsoDate(it.updatedAtUtcIso) == today }

        val dailyRevenue = (6 downTo 0).map { dayOffset ->
            val date = today.minusDays(dayOffset.toLong())
            completedSales
                .filter { parseIsoDate(it.updatedAtUtcIso) == date }
                .sumOf { it.totalAmount }
        }

        DashboardSummary(
            todayCompletedSalesCount = todayCompleted.size,
            todayRevenue = todayCompleted.sumOf { it.totalAmount },
            inventoryItems = inventory.size,
            lowStockItems = inventory.count { it.quantity <= 2 },
            openConflicts = conflicts.size,
            revenueLast7Days = dailyRevenue
        )
    }

    suspend fun fetchReportPreview(
        reportType: ReportType,
        fromDateIso: String? = null,
        toDateIso: String? = null
    ): Result<ReportPreview> = withContext(Dispatchers.IO) {
        try {
            val preview = withAuthRetry {
                when (reportType) {
                    ReportType.Inventory -> api.getInventoryReport().toPreview()
                    ReportType.Sales -> api.getSalesReport(fromDateIso, toDateIso).toPreview()
                    ReportType.ProfitLoss -> api.getProfitLossReport(fromDateIso, toDateIso).toPreview()
                    ReportType.Creditors -> api.getCreditorsReport(fromDateIso, toDateIso).toPreview()
                }
            }
            Result.success(preview)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun exportReportFile(
        reportType: ReportType,
        format: ReportExportFormat,
        fromDateIso: String? = null,
        toDateIso: String? = null
    ): Result<File> = withContext(Dispatchers.IO) {
        try {
            val body = withAuthRetry {
                api.exportReport(
                    reportType.apiName,
                    format.apiValue,
                    fromDateIso,
                    toDateIso
                )
            }

            val extension = if (format == ReportExportFormat.Pdf) "pdf" else "csv"
            val directory = File(appContext.cacheDir, "reports").apply { mkdirs() }
            val filename = "${reportType.apiName}-${System.currentTimeMillis()}.$extension"
            val file = File(directory, filename)
            file.outputStream().use { out ->
                body.byteStream().use { input ->
                    input.copyTo(out)
                }
            }
            Result.success(file)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun saveInventoryItem(input: NewInventoryInput): Result<InventoryItemEntity> = withContext(Dispatchers.IO) {
        try {
            val now = Instant.now().toString()
            val localId = UUID.randomUUID().toString()
            val localEntity = InventoryItemEntity(
                id = localId,
                productName = input.productName,
                modelNumber = input.modelNumber,
                serialNumber = input.serialNumber,
                quantity = input.quantity,
                expiryDateIso = input.expiryDateIso,
                costPrice = input.costPrice,
                sellingPrice = input.sellingPrice,
                itemType = input.itemTypeCode.toString(),
                conditionGrade = input.conditionGradeCode?.toString(),
                conditionNotes = input.conditionNotes,
                rowVersionBase64 = "",
                updatedAtUtcIso = now
            )
            db.inventoryDao().upsert(localEntity)

            val payload = input.toInventoryCreateRequest()

            db.syncDao().enqueue(
                SyncQueueEntity(
                    entityName = QueueEntity.InventoryCreate,
                    entityId = localId,
                    operation = QueueOperation.Create,
                    payloadJson = inventoryCreateAdapter.toJson(payload),
                    rowVersionBase64 = null,
                    enqueuedAtUtcIso = now
                )
            )

            input.photoUris.forEach { uri ->
                db.pendingItemPhotoDao().insert(
                    PendingItemPhotoEntity(
                        localItemId = localId,
                        photoUri = uri,
                        createdAtUtcIso = now
                    )
                )
            }

            runSyncOnce()
            Result.success(localEntity)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun updateInventoryItem(itemId: String, input: NewInventoryInput): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val existing = db.inventoryDao().getById(itemId)
                ?: return@withContext Result.failure(IllegalArgumentException("Inventory item not found."))

            val now = Instant.now().toString()
            val updated = existing.copy(
                productName = input.productName,
                modelNumber = input.modelNumber,
                serialNumber = input.serialNumber,
                quantity = input.quantity,
                expiryDateIso = input.expiryDateIso,
                costPrice = input.costPrice,
                sellingPrice = input.sellingPrice,
                itemType = input.itemTypeCode.toString(),
                conditionGrade = input.conditionGradeCode?.toString(),
                conditionNotes = input.conditionNotes,
                updatedAtUtcIso = now
            )
            db.inventoryDao().upsert(updated)

            val payload = input.toInventoryCreateRequest()
            db.syncDao().deleteByEntityId(itemId)

            val isUnsyncedLocal = existing.rowVersionBase64.isBlank()
            val queueEntityName = if (isUnsyncedLocal) QueueEntity.InventoryCreate else QueueEntity.InventorySync
            val queueOperation = if (isUnsyncedLocal) QueueOperation.Create else QueueOperation.Update

            db.syncDao().enqueue(
                SyncQueueEntity(
                    entityName = queueEntityName,
                    entityId = itemId,
                    operation = queueOperation,
                    payloadJson = inventoryCreateAdapter.toJson(payload),
                    rowVersionBase64 = existing.rowVersionBase64.ifBlank { null },
                    enqueuedAtUtcIso = now
                )
            )

            runSyncOnce()
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun deleteInventoryItem(itemId: String): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val existing = db.inventoryDao().getById(itemId)
                ?: return@withContext Result.failure(IllegalArgumentException("Inventory item not found."))

            val now = Instant.now().toString()
            db.inventoryDao().deleteById(itemId)
            db.pendingItemPhotoDao().deleteByLocalItem(itemId)
            db.syncDao().deleteByEntityId(itemId)

            if (existing.rowVersionBase64.isNotBlank()) {
                db.syncDao().enqueue(
                    SyncQueueEntity(
                        entityName = QueueEntity.InventorySync,
                        entityId = itemId,
                        operation = QueueOperation.Delete,
                        payloadJson = "{}",
                        rowVersionBase64 = existing.rowVersionBase64,
                        enqueuedAtUtcIso = now
                    )
                )
            }

            runSyncOnce()
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun recordSale(input: NewSaleInput): Result<RecordedSale> = withContext(Dispatchers.IO) {
        try {
            val now = Instant.now().toString()
            val localSaleId = UUID.randomUUID().toString()
            val saleLines = input.lines
                .filter { it.quantity > 0 && it.unitPrice >= 0.0 }
                .map {
                    SaleLineRequest(
                        inventoryItemId = it.inventoryItemId,
                        quantity = it.quantity,
                        unitPrice = it.unitPrice
                    )
                }
            require(saleLines.isNotEmpty()) { "Sale must contain at least one line item." }

            val subtotal = saleLines.sumOf { it.unitPrice * it.quantity }
            val total = (subtotal - input.discountAmount).coerceAtLeast(0.0)
            val outstanding = if (input.isCredit) {
                (total - input.paidAmount).coerceAtLeast(0.0)
            } else {
                0.0
            }
            val lineSummary = input.lines.joinToString(separator = ", ") { line ->
                "${line.productName} x${line.quantity}"
            }

            val request = CreateSaleRequest(
                customerName = input.customerName,
                customerPhone = input.customerPhone,
                discountAmount = input.discountAmount,
                isCredit = input.isCredit,
                dueDateUtc = input.dueDateUtcIso,
                lines = saleLines,
                initialPayments = listOf(
                    SalePaymentRequest(
                        method = input.paymentMethodCode,
                        amount = input.paidAmount,
                        reference = input.paymentReference
                    )
                )
            )

            db.salesDao().upsert(
                SaleEntity(
                    id = localSaleId,
                    saleNumber = "LOCAL-${System.currentTimeMillis()}",
                    customerName = input.customerName,
                    customerPhone = input.customerPhone,
                    lineItemsSummary = lineSummary,
                    subtotal = subtotal,
                    vatAmount = 0.0,
                    discountAmount = input.discountAmount,
                    totalAmount = total,
                    outstandingAmount = outstanding,
                    status = if (outstanding > 0) "PARTIALLY_PAID" else "COMPLETED",
                    isCredit = input.isCredit,
                    dueDateUtcIso = input.dueDateUtcIso,
                    updatedAtUtcIso = now
                )
            )

            db.syncDao().enqueue(
                SyncQueueEntity(
                    entityName = QueueEntity.SaleCreate,
                    entityId = localSaleId,
                    operation = QueueOperation.Create,
                    payloadJson = saleCreateAdapter.toJson(request),
                    rowVersionBase64 = null,
                    enqueuedAtUtcIso = now
                )
            )

            val syncSummary = runSyncOnce()
            val latestSale = db.salesDao().getAll().firstOrNull()
            Result.success(
                RecordedSale(
                    id = latestSale?.id ?: localSaleId,
                    saleNumber = latestSale?.saleNumber ?: "LOCAL-PENDING",
                    totalAmount = latestSale?.totalAmount ?: request.lines.sumOf { it.unitPrice * it.quantity },
                    outstandingAmount = latestSale?.outstandingAmount ?: 0.0,
                    synced = syncSummary.accepted > 0 && syncSummary.transientFailures == 0
                )
            )
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun addCreditRepayment(input: CreditRepaymentInput): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val amount = input.amount.coerceAtLeast(0.0)
            require(amount > 0.0) { "Repayment amount must be greater than zero." }

            val existingSale = db.salesDao().getById(input.saleId)
                ?: return@withContext Result.failure(IllegalArgumentException("Credit sale not found."))
            if (!existingSale.isCredit) {
                return@withContext Result.failure(IllegalArgumentException("Selected sale is not a credit sale."))
            }

            val now = Instant.now().toString()
            val newOutstanding = (existingSale.outstandingAmount - amount).coerceAtLeast(0.0)
            db.salesDao().upsert(
                existingSale.copy(
                    outstandingAmount = newOutstanding,
                    status = if (newOutstanding <= 0.0) "COMPLETED" else "PARTIALLY_PAID",
                    updatedAtUtcIso = now
                )
            )

            val payload = CreditRepaymentEnvelope(
                saleId = input.saleId,
                request = CreditRepaymentRequest(
                    amount = amount,
                    method = input.paymentMethodCode,
                    reference = input.reference,
                    notes = input.notes
                )
            )

            db.syncDao().enqueue(
                SyncQueueEntity(
                    entityName = QueueEntity.CreditRepaymentCreate,
                    entityId = input.saleId,
                    operation = QueueOperation.Create,
                    payloadJson = repaymentAdapter.toJson(payload),
                    rowVersionBase64 = null,
                    enqueuedAtUtcIso = now
                )
            )

            runSyncOnce()
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    suspend fun getConflicts(): List<SyncConflictEntity> = withContext(Dispatchers.IO) {
        db.syncDao().getConflicts()
    }

    suspend fun resolveConflictKeepServer(conflictId: Long) = withContext(Dispatchers.IO) {
        db.syncDao().deleteConflictById(conflictId)
    }

    suspend fun resolveConflictKeepLocal(conflictId: Long) = withContext(Dispatchers.IO) {
        val conflict = db.syncDao().getConflictById(conflictId) ?: return@withContext
        db.syncDao().enqueue(
            SyncQueueEntity(
                entityName = conflict.entityName,
                entityId = conflict.entityId,
                operation = QueueOperation.Create,
                payloadJson = conflict.localPayloadJson,
                rowVersionBase64 = null,
                enqueuedAtUtcIso = Instant.now().toString(),
                retryCount = 0
            )
        )
        db.syncDao().deleteConflictById(conflictId)
    }

    suspend fun runSyncOnce(): SyncRunSummary = withContext(Dispatchers.IO) {
        val pending = db.syncDao().getPending(limit = 50)
        if (pending.isEmpty()) {
            val pullApplied = runCatching { pullAndApplyServerChanges() }.getOrElse { 0 }
            return@withContext SyncRunSummary(pullApplied, 0, 0)
        }

        var accepted = 0
        var conflicts = 0
        var transientFailures = 0

        pending.forEach { change ->
            try {
                when (change.entityName) {
                    QueueEntity.InventoryCreate -> syncInventoryCreate(change)
                    QueueEntity.SaleCreate -> syncSaleCreate(change)
                    QueueEntity.CreditRepaymentCreate -> syncCreditRepayment(change)
                    else -> pushGenericSync(change)
                }
                db.syncDao().deleteById(change.id)
                accepted++
            } catch (httpEx: HttpException) {
                if (httpEx.code() == 409) {
                    db.syncDao().addConflict(
                        SyncConflictEntity(
                            entityName = change.entityName,
                            entityId = change.entityId,
                            serverPayloadJson = httpEx.response()?.errorBody()?.string().orEmpty(),
                            localPayloadJson = change.payloadJson,
                            conflictReason = "Conflict detected (${httpEx.code()})",
                            createdAtUtcIso = Instant.now().toString()
                        )
                    )
                    if (change.entityName == QueueEntity.InventoryCreate) {
                        db.pendingItemPhotoDao().deleteByLocalItem(change.entityId)
                    }
                    db.syncDao().deleteById(change.id)
                    conflicts++
                } else {
                    transientFailures++
                    db.syncDao().incrementRetryCount(change.id)
                }
            } catch (_: Exception) {
                transientFailures++
                db.syncDao().incrementRetryCount(change.id)
            }
        }

        val pullApplied = runCatching { pullAndApplyServerChanges() }
            .getOrElse {
                transientFailures++
                0
            }

        SyncRunSummary(accepted + pullApplied, conflicts, transientFailures)
    }

    private suspend fun syncInventoryCreate(change: SyncQueueEntity) {
        val payload = inventoryCreateAdapter.fromJson(change.payloadJson)
            ?: error("Invalid inventory payload")

        val pendingPhotos = db.pendingItemPhotoDao().getForLocalItem(change.entityId)
        val response = withAuthRetry { api.createInventoryItem(payload) }
        pendingPhotos.forEach { photo ->
            withAuthRetry {
                api.addInventoryItemPhoto(
                    response.id,
                    AddItemPhotoRequest(photo.photoUri)
                )
            }
            db.pendingItemPhotoDao().deleteById(photo.id)
        }

        db.inventoryDao().deleteById(change.entityId)
        db.inventoryDao().upsert(response.toEntity())
    }

    private suspend fun syncSaleCreate(change: SyncQueueEntity) {
        val payload = saleCreateAdapter.fromJson(change.payloadJson)
            ?: error("Invalid sale payload")

        val response = withAuthRetry { api.createSale(payload) }

        db.salesDao().deleteById(change.entityId)
        db.salesDao().upsert(response.toEntity(payload))
    }

    private suspend fun syncCreditRepayment(change: SyncQueueEntity) {
        val payload = repaymentAdapter.fromJson(change.payloadJson)
            ?: error("Invalid credit repayment payload")

        withAuthRetry {
            api.addCreditRepayment(payload.saleId, payload.request)
        }
    }

    private suspend fun pushGenericSync(change: SyncQueueEntity) {
        withAuthRetry {
            api.pushChanges(
                SyncPushRequest(
                    changes = listOf(
                        SyncPushChange(
                            deviceId = deviceId(),
                            entityName = change.entityName,
                            entityId = change.entityId,
                            operation = when (change.operation) {
                                QueueOperation.Create -> 1
                                QueueOperation.Update -> 2
                                QueueOperation.Delete -> 3
                                else -> 1
                            },
                            payloadJson = change.payloadJson,
                            clientUpdatedAtUtc = change.enqueuedAtUtcIso,
                            rowVersionBase64 = change.rowVersionBase64
                        )
                    )
                )
            )
        }
    }

    private suspend fun pullAndApplyServerChanges(): Int {
        val response = withAuthRetry {
            api.pullChanges(
                SyncPullRequest(
                    deviceId = deviceId(),
                    sinceUtc = syncPrefs.getString(KEY_LAST_PULL_UTC, null)
                )
            )
        }

        var applied = 0
        response.changes.forEach { change ->
            val changed = when (change.entityName) {
                "InventoryItem" -> applyInventoryPullChange(change)
                "Sale" -> applySalePullChange(change)
                else -> false
            }
            if (changed) {
                applied++
            }
        }

        syncPrefs.edit().putString(KEY_LAST_PULL_UTC, response.serverTimestampUtc).apply()
        return applied
    }

    private suspend fun applyInventoryPullChange(change: SyncPushChange): Boolean {
        if (change.operation == 3) {
            db.inventoryDao().deleteById(change.entityId)
            return true
        }

        val payload = inventoryResponseAdapter.fromJson(change.payloadJson) ?: return false
        db.inventoryDao().upsert(payload.toEntity())
        return true
    }

    private suspend fun applySalePullChange(change: SyncPushChange): Boolean {
        if (change.operation == 3) {
            db.salesDao().deleteById(change.entityId)
            return true
        }

        val payload = saleSyncPayloadAdapter.fromJson(change.payloadJson) ?: return false
        db.salesDao().upsert(payload.toEntity())
        return true
    }

    private suspend fun <T> withAuthRetry(block: suspend () -> T): T {
        ensureAuthenticated(forceRefresh = false)
        try {
            return block()
        } catch (httpEx: HttpException) {
            if (httpEx.code() == 401 || httpEx.code() == 403) {
                ensureAuthenticated(forceRefresh = true)
                return block()
            }
            throw httpEx
        }
    }

    private suspend fun ensureAuthenticated(forceRefresh: Boolean) {
        if (forceRefresh) {
            sessionManager.clear()
        }

        if (!sessionManager.accessToken().isNullOrBlank()) {
            return
        }

        authenticateWithBootstrap(DefaultOwner.Email, DefaultOwner.ShopName)
    }

    private suspend fun authenticateWithBootstrap(email: String, shopName: String) {
        val login = LoginRequest(
            login = email,
            password = DefaultOwner.Password,
            shopId = null
        )

        try {
            val auth = api.login(login)
            sessionManager.saveSession(auth.accessToken, auth.refreshToken, auth.shopId, auth.role)
            return
        } catch (_: Exception) {
        }

        try {
            api.registerOwner(
                RegisterOwnerRequest(
                    fullName = DefaultOwner.FullName,
                    email = email,
                    phone = null,
                    password = DefaultOwner.Password,
                    shopName = shopName,
                    vatEnabled = true,
                    vatRate = 0.075
                )
            )
        } catch (_: Exception) {
        }

        try {
            val auth = api.login(login)
            sessionManager.saveSession(auth.accessToken, auth.refreshToken, auth.shopId, auth.role)
            return
        } catch (_: Exception) {
        }

        val fallbackEmail = "owner.${System.currentTimeMillis()}@shopkeeper.local"
        val fallbackShop = "My Shop ${System.currentTimeMillis() % 10000}"

        api.registerOwner(
            RegisterOwnerRequest(
                fullName = DefaultOwner.FullName,
                email = fallbackEmail,
                phone = null,
                password = DefaultOwner.Password,
                shopName = fallbackShop,
                vatEnabled = true,
                vatRate = 0.075
            )
        )

        val finalAuth = api.login(
            LoginRequest(
                login = fallbackEmail,
                password = DefaultOwner.Password,
                shopId = null
            )
        )
        sessionManager.saveSession(finalAuth.accessToken, finalAuth.refreshToken, finalAuth.shopId, finalAuth.role)
    }

    private fun parseIsoDate(iso: String): LocalDate? {
        return runCatching {
            Instant.parse(iso).atZone(ZoneId.systemDefault()).toLocalDate()
        }.getOrNull()
    }

    private fun deviceId(): String {
        return Settings.Secure.getString(appContext.contentResolver, Settings.Secure.ANDROID_ID)
            ?: "unknown-device"
    }

    private fun InventoryItemResponse.toEntity(): InventoryItemEntity {
        return InventoryItemEntity(
            id = id,
            productName = productName,
            modelNumber = modelNumber,
            serialNumber = serialNumber,
            quantity = quantity,
            expiryDateIso = expiryDate,
            costPrice = costPrice,
            sellingPrice = sellingPrice,
            itemType = itemType.toString(),
            conditionGrade = conditionGrade?.toString(),
            conditionNotes = conditionNotes,
            rowVersionBase64 = rowVersionBase64,
            updatedAtUtcIso = Instant.now().toString()
        )
    }

    private fun NewInventoryInput.toInventoryCreateRequest(): CreateInventoryItemRequest {
        return CreateInventoryItemRequest(
            productName = productName,
            modelNumber = modelNumber,
            serialNumber = serialNumber,
            quantity = quantity,
            expiryDate = expiryDateIso,
            costPrice = costPrice,
            sellingPrice = sellingPrice,
            itemType = itemTypeCode,
            conditionGrade = conditionGradeCode,
            conditionNotes = conditionNotes
        )
    }

    private fun CreateSaleResponse.toEntity(request: CreateSaleRequest): SaleEntity {
        return SaleEntity(
            id = id,
            saleNumber = saleNumber,
            customerName = request.customerName,
            customerPhone = request.customerPhone,
            lineItemsSummary = request.lines.joinToString(", ") { "${it.inventoryItemId} x${it.quantity}" },
            subtotal = request.lines.sumOf { it.unitPrice * it.quantity },
            vatAmount = 0.0,
            discountAmount = request.discountAmount,
            totalAmount = totalAmount,
            outstandingAmount = outstandingAmount,
            status = if (outstandingAmount > 0) "PARTIALLY_PAID" else "COMPLETED",
            isCredit = request.isCredit,
            dueDateUtcIso = request.dueDateUtc,
            updatedAtUtcIso = Instant.now().toString()
        )
    }

    private fun SaleSyncPayload.toEntity(): SaleEntity {
        return SaleEntity(
            id = id,
            saleNumber = saleNumber,
            customerName = null,
            customerPhone = null,
            lineItemsSummary = "",
            subtotal = subtotal,
            vatAmount = vatAmount,
            discountAmount = discountAmount,
            totalAmount = totalAmount,
            outstandingAmount = outstandingAmount,
            status = normalizeSaleStatus(status),
            isCredit = isCredit,
            dueDateUtcIso = dueDateUtc,
            updatedAtUtcIso = updatedAtUtc
        )
    }

    private fun InventoryReportResponseDto.toPreview(): ReportPreview {
        val lines = buildList {
            add("Products: $totalProducts")
            add("Units: $totalUnits")
            add("Low Stock: $lowStockItems")
            add("Cost Value: NGN ${"%.2f".format(totalCostValue)}")
            add("Selling Value: NGN ${"%.2f".format(totalSellingValue)}")
            add("")
            items.take(20).forEach {
                add("${it.productName} | Qty ${it.quantity} | Cost ${"%.2f".format(it.costPrice)} | Sell ${"%.2f".format(it.sellingPrice)}")
            }
        }
        return ReportPreview("Inventory Report", lines)
    }

    private fun SalesReportResponseDto.toPreview(): ReportPreview {
        val lines = buildList {
            add("Range: ${fromUtc.take(10)} to ${toUtc.take(10)}")
            add("Sales Count: $salesCount")
            add("Revenue: NGN ${"%.2f".format(revenue)}")
            add("VAT: NGN ${"%.2f".format(vatAmount)}")
            add("Discount: NGN ${"%.2f".format(discountAmount)}")
            add("Outstanding: NGN ${"%.2f".format(outstandingAmount)}")
            add("")
            daily.take(14).forEach {
                add("${it.date.take(10)} | Sales ${it.salesCount} | Revenue ${"%.2f".format(it.revenue)}")
            }
        }
        return ReportPreview("Sales Report", lines)
    }

    private fun ProfitLossReportResponseDto.toPreview(): ReportPreview {
        val lines = listOf(
            "Range: ${fromUtc.take(10)} to ${toUtc.take(10)}",
            "Revenue: NGN ${"%.2f".format(revenue)}",
            "COGS: NGN ${"%.2f".format(cogs)}",
            "Gross Profit: NGN ${"%.2f".format(grossProfit)}",
            "Expenses: NGN ${"%.2f".format(expenses)}",
            "Net Profit/Loss: NGN ${"%.2f".format(netProfitLoss)}"
        )
        return ReportPreview("Profit & Loss", lines)
    }

    private fun CreditorsReportResponseDto.toPreview(): ReportPreview {
        val lines = buildList {
            add("Open Credits: $openCredits")
            add("Total Outstanding: NGN ${"%.2f".format(totalOutstanding)}")
            add("")
            credits.take(20).forEach {
                add("${it.customerName} | ${it.saleNumber} | Due ${it.dueDateUtc.take(10)} | Out ${"%.2f".format(it.outstandingAmount)}")
            }
        }
        return ReportPreview("Creditors Report", lines)
    }

    private fun normalizeSaleStatus(value: String): String {
        return when (value.trim().uppercase()) {
            "COMPLETED" -> "COMPLETED"
            "PARTIALLYPAID", "PARTIALLY_PAID" -> "PARTIALLY_PAID"
            "VOID" -> "VOID"
            else -> value.uppercase()
        }
    }

    private fun AccountProfileResponseDto.toModel(): AccountProfile {
        return AccountProfile(
            userId = userId,
            fullName = fullName,
            email = email,
            phone = phone,
            avatarUrl = avatarUrl,
            preferredLanguage = preferredLanguage ?: "en",
            timezone = timezone ?: "UTC",
            createdAtUtc = createdAtUtc
        )
    }

    private fun SessionViewDto.toModel(): AccountSession {
        return AccountSession(
            sessionId = sessionId,
            shopId = shopId,
            role = role,
            deviceId = deviceId,
            deviceName = deviceName,
            createdAtUtc = createdAtUtc,
            expiresAtUtc = expiresAtUtc,
            lastSeenAtUtc = lastSeenAtUtc,
            isRevoked = isRevoked
        )
    }

    private fun LinkedIdentityViewDto.toModel(): LinkedIdentity {
        return LinkedIdentity(
            provider = provider,
            providerSubject = providerSubject,
            email = email,
            emailVerified = emailVerified,
            createdAtUtc = createdAtUtc,
            lastUsedAtUtc = lastUsedAtUtc
        )
    }

    private fun MagicLinkRequestResponseDto.toModel(): MagicLinkChallenge {
        return MagicLinkChallenge(
            requestId = requestId,
            expiresAtUtc = expiresAtUtc,
            message = message,
            debugToken = debugToken
        )
    }

    private fun CreditDetailResponseDto.toModel(): CreditDetail {
        return CreditDetail(
            saleId = account.saleId,
            outstandingAmount = account.outstandingAmount,
            repayments = repayments.map { it.toModel() }
        )
    }

    private fun CreditRepaymentViewDto.toModel(): CreditRepaymentRecord {
        return CreditRepaymentRecord(
            repaymentId = id,
            amount = amount,
            paymentMethodCode = method,
            reference = reference,
            notes = notes,
            createdAtUtc = createdAtUtc
        )
    }

    companion object {
        @Volatile
        private var instance: ShopkeeperDataGateway? = null

        fun get(context: Context): ShopkeeperDataGateway {
            return instance ?: synchronized(this) {
                instance ?: ShopkeeperDataGateway(context.applicationContext).also { instance = it }
            }
        }
    }
}

data class DashboardSummary(
    val todayCompletedSalesCount: Int,
    val todayRevenue: Double,
    val inventoryItems: Int,
    val lowStockItems: Int,
    val openConflicts: Int,
    val revenueLast7Days: List<Double>
)

data class AccountProfile(
    val userId: String,
    val fullName: String,
    val email: String?,
    val phone: String?,
    val avatarUrl: String?,
    val preferredLanguage: String,
    val timezone: String,
    val createdAtUtc: String
)

data class AccountProfileUpdateInput(
    val fullName: String,
    val phone: String?,
    val avatarUrl: String?,
    val preferredLanguage: String?,
    val timezone: String?
)

data class AccountSession(
    val sessionId: String,
    val shopId: String,
    val role: String,
    val deviceId: String?,
    val deviceName: String?,
    val createdAtUtc: String,
    val expiresAtUtc: String,
    val lastSeenAtUtc: String?,
    val isRevoked: Boolean
)

data class LinkedIdentity(
    val provider: String,
    val providerSubject: String,
    val email: String?,
    val emailVerified: Boolean,
    val createdAtUtc: String,
    val lastUsedAtUtc: String
)

data class MagicLinkChallenge(
    val requestId: String,
    val expiresAtUtc: String,
    val message: String,
    val debugToken: String?
)

data class ReportPreview(
    val title: String,
    val lines: List<String>
)

enum class ReportType(val apiName: String, val label: String) {
    Inventory("inventory", "Inventory"),
    Sales("sales", "Sales"),
    ProfitLoss("profit-loss", "Profit & Loss"),
    Creditors("creditors", "Creditors")
}

enum class ReportExportFormat(val apiValue: String, val label: String, val mimeType: String) {
    Pdf("pdf", "PDF", "application/pdf"),
    Spreadsheet("spreadsheet", "Spreadsheet", "text/csv")
}

data class NewInventoryInput(
    val productName: String,
    val modelNumber: String?,
    val serialNumber: String?,
    val quantity: Int,
    val expiryDateIso: String?,
    val costPrice: Double,
    val sellingPrice: Double,
    val itemTypeCode: Int,
    val conditionGradeCode: Int?,
    val conditionNotes: String?,
    val photoUris: List<String> = emptyList()
)

data class NewSaleInput(
    val lines: List<NewSaleLineInput>,
    val discountAmount: Double,
    val paidAmount: Double,
    val paymentMethodCode: Int,
    val paymentReference: String?,
    val customerName: String?,
    val customerPhone: String?,
    val isCredit: Boolean,
    val dueDateUtcIso: String?
)

data class NewSaleLineInput(
    val inventoryItemId: String,
    val productName: String,
    val quantity: Int,
    val unitPrice: Double
)

data class CreditRepaymentInput(
    val saleId: String,
    val amount: Double,
    val paymentMethodCode: Int,
    val reference: String?,
    val notes: String?
)

data class RecordedSale(
    val id: String,
    val saleNumber: String,
    val totalAmount: Double,
    val outstandingAmount: Double,
    val synced: Boolean
)

data class CreditSaleOption(
    val saleId: String,
    val saleNumber: String,
    val customerName: String,
    val itemSummary: String,
    val outstandingAmount: Double
)

data class CreditDetail(
    val saleId: String,
    val outstandingAmount: Double,
    val repayments: List<CreditRepaymentRecord>
)

data class CreditRepaymentRecord(
    val repaymentId: String,
    val amount: Double,
    val paymentMethodCode: Int,
    val reference: String?,
    val notes: String?,
    val createdAtUtc: String
)

data class SyncRunSummary(
    val accepted: Int,
    val conflicts: Int,
    val transientFailures: Int
)

private data class CreditRepaymentEnvelope(
    val saleId: String,
    val request: CreditRepaymentRequest
)

private object DefaultOwner {
    const val FullName = "Shop Owner"
    const val Email = "owner@shopkeeper.local"
    const val Password = "Shopkeeper123!"
    const val ShopName = "My Shop"
}

private object QueueEntity {
    const val InventoryCreate = "inventory.create"
    const val InventorySync = "InventoryItem"
    const val SaleCreate = "sale.create"
    const val CreditRepaymentCreate = "credit.repayment.create"
}

private const val KEY_LAST_PULL_UTC = "last_pull_utc"

private object QueueOperation {
    const val Create = "CREATE"
    const val Update = "UPDATE"
    const val Delete = "DELETE"
}
