package com.shopkeeper.mobile.core.data.local

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase

@Database(
    entities = [
        InventoryItemEntity::class,
        SaleEntity::class,
        ReceiptFileEntity::class,
        SyncQueueEntity::class,
        SyncConflictEntity::class,
        PendingItemPhotoEntity::class
    ],
    version = 4,
    exportSchema = false
)
abstract class ShopkeeperDatabase : RoomDatabase() {
    abstract fun inventoryDao(): InventoryDao
    abstract fun salesDao(): SalesDao
    abstract fun receiptDao(): ReceiptDao
    abstract fun syncDao(): SyncDao
    abstract fun pendingItemPhotoDao(): PendingItemPhotoDao

    companion object {
        @Volatile
        private var instance: ShopkeeperDatabase? = null

        fun get(context: Context): ShopkeeperDatabase {
            return instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(
                    context.applicationContext,
                    ShopkeeperDatabase::class.java,
                    "shopkeeper-mobile.db"
                )
                    .fallbackToDestructiveMigration()
                    .build()
                    .also { instance = it }
            }
        }

        fun closeAndClear() {
            synchronized(this) {
                instance?.close()
                instance = null
            }
        }
    }
}
