package com.shopkeeper.mobile.core.data.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

@Dao
interface InventoryDao {
    @Query("SELECT * FROM inventory_items ORDER BY updatedAtUtcIso DESC")
    suspend fun getAll(): List<InventoryItemEntity>

    @Query("SELECT * FROM inventory_items WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): InventoryItemEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(item: InventoryItemEntity)

    @Query("DELETE FROM inventory_items WHERE id = :id")
    suspend fun deleteById(id: String)
}

@Dao
interface SalesDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(sale: SaleEntity)

    @Query("SELECT * FROM sales ORDER BY updatedAtUtcIso DESC")
    suspend fun getAll(): List<SaleEntity>

    @Query("SELECT * FROM sales WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): SaleEntity?

    @Query("DELETE FROM sales WHERE id = :id")
    suspend fun deleteById(id: String)
}

@Dao
interface ReceiptDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(receipt: ReceiptFileEntity)

    @Query("SELECT * FROM receipt_files WHERE localSaleId = :localSaleId ORDER BY receiptKind ASC")
    suspend fun getByLocalSale(localSaleId: String): List<ReceiptFileEntity>

    @Query("SELECT * FROM receipt_files WHERE localSaleId = :localSaleId AND receiptKind = :receiptKind LIMIT 1")
    suspend fun getByLocalSaleAndKind(localSaleId: String, receiptKind: String): ReceiptFileEntity?

    @Query("SELECT * FROM receipt_files WHERE serverSaleId = :serverSaleId ORDER BY updatedAtUtcIso DESC LIMIT 1")
    suspend fun getByServerSaleId(serverSaleId: String): ReceiptFileEntity?
}

@Dao
interface SyncDao {
    @Insert
    suspend fun enqueue(change: SyncQueueEntity)

    @Query("SELECT * FROM sync_queue ORDER BY id ASC LIMIT :limit")
    suspend fun getPending(limit: Int): List<SyncQueueEntity>

    @Query("DELETE FROM sync_queue WHERE entityId = :entityId")
    suspend fun deleteByEntityId(entityId: String)

    @Query("DELETE FROM sync_queue WHERE id = :id")
    suspend fun deleteById(id: Long)

    @Query("UPDATE sync_queue SET retryCount = retryCount + 1 WHERE id = :id")
    suspend fun incrementRetryCount(id: Long)

    @Query(
        """
        UPDATE sync_queue
        SET claimToken = :claimToken,
            claimedAtEpochMs = :claimedAtEpochMs
        WHERE id = :id
          AND (
            claimToken IS NULL
            OR claimedAtEpochMs IS NULL
            OR claimedAtEpochMs < :staleBeforeEpochMs
          )
        """
    )
    suspend fun claimRow(
        id: Long,
        claimToken: String,
        claimedAtEpochMs: Long,
        staleBeforeEpochMs: Long
    ): Int

    @Query(
        """
        UPDATE sync_queue
        SET claimToken = NULL,
            claimedAtEpochMs = NULL
        WHERE id = :id AND claimToken = :claimToken
        """
    )
    suspend fun releaseClaim(id: Long, claimToken: String): Int

    @Query(
        """
        UPDATE sync_queue
        SET claimToken = NULL,
            claimedAtEpochMs = NULL,
            retryCount = retryCount + 1
        WHERE id = :id AND claimToken = :claimToken
        """
    )
    suspend fun releaseClaimAndIncrementRetry(id: Long, claimToken: String): Int

    @Query("DELETE FROM sync_queue WHERE id = :id AND claimToken = :claimToken")
    suspend fun deleteClaimedRow(id: Long, claimToken: String): Int

    @Query(
        """
        UPDATE sync_queue
        SET claimToken = NULL,
            claimedAtEpochMs = NULL
        WHERE claimedAtEpochMs IS NOT NULL
          AND claimedAtEpochMs < :staleBeforeEpochMs
        """
    )
    suspend fun releaseStaleClaims(staleBeforeEpochMs: Long): Int

    @Insert
    suspend fun addConflict(conflict: SyncConflictEntity)

    @Query("SELECT * FROM sync_conflicts ORDER BY id DESC")
    suspend fun getConflicts(): List<SyncConflictEntity>

    @Query("SELECT * FROM sync_conflicts WHERE id = :id LIMIT 1")
    suspend fun getConflictById(id: Long): SyncConflictEntity?

    @Query("DELETE FROM sync_conflicts WHERE entityName = :entityName AND entityId = :entityId")
    suspend fun deleteConflictsByEntity(entityName: String, entityId: String)

    @Query("DELETE FROM sync_conflicts WHERE id = :id")
    suspend fun deleteConflictById(id: Long)
}

@Dao
interface PendingItemPhotoDao {
    @Insert
    suspend fun insert(photo: PendingItemPhotoEntity)

    @Query("SELECT * FROM pending_item_photos WHERE localItemId = :localItemId ORDER BY id ASC")
    suspend fun getForLocalItem(localItemId: String): List<PendingItemPhotoEntity>

    @Query("DELETE FROM pending_item_photos WHERE id = :id")
    suspend fun deleteById(id: Long)

    @Query("DELETE FROM pending_item_photos WHERE localItemId = :localItemId")
    suspend fun deleteByLocalItem(localItemId: String)
}
