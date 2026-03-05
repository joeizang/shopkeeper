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
import com.shopkeeper.mobile.core.data.remote.CreateInventoryItemRequest
import com.shopkeeper.mobile.core.data.remote.CreateSaleRequest
import com.shopkeeper.mobile.core.data.remote.CreateSaleResponse
import com.shopkeeper.mobile.core.data.remote.CreditRepaymentRequest
import com.shopkeeper.mobile.core.data.remote.InventoryItemResponse
import com.shopkeeper.mobile.core.data.remote.LoginRequest
import com.shopkeeper.mobile.core.data.remote.NetworkFactory
import com.shopkeeper.mobile.core.data.remote.RegisterOwnerRequest
import com.shopkeeper.mobile.core.data.remote.SaleSyncPayload
import com.shopkeeper.mobile.core.data.remote.SaleLineRequest
import com.shopkeeper.mobile.core.data.remote.SalePaymentRequest
import com.shopkeeper.mobile.core.data.remote.SyncPushChange
import com.shopkeeper.mobile.core.data.remote.SyncPullRequest
import com.shopkeeper.mobile.core.data.remote.SyncPushRequest
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import retrofit2.HttpException
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

    fun isOwnerSession(): Boolean {
        val role = sessionManager.role()
        if (role.isNullOrBlank()) {
            return true
        }
        return role.equals("Owner", ignoreCase = true)
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

            val payload = CreateInventoryItemRequest(
                productName = input.productName,
                modelNumber = input.modelNumber,
                serialNumber = input.serialNumber,
                quantity = input.quantity,
                expiryDate = input.expiryDateIso,
                costPrice = input.costPrice,
                sellingPrice = input.sellingPrice,
                itemType = input.itemTypeCode,
                conditionGrade = input.conditionGradeCode,
                conditionNotes = input.conditionNotes
            )

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
            val payload = CreditRepaymentEnvelope(
                saleId = input.saleId,
                request = CreditRepaymentRequest(
                    amount = input.amount,
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
                    enqueuedAtUtcIso = Instant.now().toString()
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

    private fun normalizeSaleStatus(value: String): String {
        return when (value.trim().uppercase()) {
            "COMPLETED" -> "COMPLETED"
            "PARTIALLYPAID", "PARTIALLY_PAID" -> "PARTIALLY_PAID"
            "VOID" -> "VOID"
            else -> value.uppercase()
        }
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
    const val SaleCreate = "sale.create"
    const val CreditRepaymentCreate = "credit.repayment.create"
}

private const val KEY_LAST_PULL_UTC = "last_pull_utc"

private object QueueOperation {
    const val Create = "CREATE"
    const val Update = "UPDATE"
    const val Delete = "DELETE"
}
