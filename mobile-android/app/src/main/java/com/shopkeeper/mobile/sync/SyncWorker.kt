package com.shopkeeper.mobile.sync

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.shopkeeper.mobile.core.data.ShopkeeperDataGateway

class SyncWorker(
    appContext: Context,
    params: WorkerParameters
) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val gateway = ShopkeeperDataGateway.get(applicationContext)
        val summary = gateway.runSyncOnce()

        return if (summary.transientFailures > 0) {
            Result.retry()
        } else {
            Result.success()
        }
    }
}
