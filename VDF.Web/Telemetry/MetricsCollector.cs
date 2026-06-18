using System.Diagnostics.Metrics;

namespace VDF.Web.Telemetry;

public sealed class MetricsCollector {
	private readonly Counter<long> _scansTotal;
	private readonly Counter<long> _duplicatesFound;
	private readonly Histogram<double> _scanDuration;
	private readonly Counter<long> _filesProcessedTotal;
	private readonly Counter<long> _apiRequestsTotal;
	private readonly Counter<long> _webhookDispatchesTotal;

	public MetricsCollector(IMeterFactory meterFactory) {
		var meter = meterFactory.Create("VDF.Web");

		_scansTotal = meter.CreateCounter<long>(
			"vdf_scans_total",
			description: "Total number of scans initiated");

		_duplicatesFound = meter.CreateCounter<long>(
			"vdf_duplicates_found",
			description: "Total number of duplicates found");

		_scanDuration = meter.CreateHistogram<double>(
			"vdf_scan_duration_seconds",
			unit: "s",
			description: "Duration of scan operations in seconds");

		_filesProcessedTotal = meter.CreateCounter<long>(
			"vdf_files_processed_total",
			description: "Total number of files processed");

		_apiRequestsTotal = meter.CreateCounter<long>(
			"vdf_api_requests_total",
			description: "Total number of API requests");

		_webhookDispatchesTotal = meter.CreateCounter<long>(
			"vdf_webhook_dispatches_total",
			description: "Total number of webhook dispatches");
	}

	public void RecordScanStarted() => _scansTotal.Add(1, new KeyValuePair<string, object?>("status", "started"));

	public void RecordScanCompleted(double durationSeconds) {
		_scansTotal.Add(1, new KeyValuePair<string, object?>("status", "completed"));
		_scanDuration.Record(durationSeconds);
	}

	public void RecordScanFailed(double durationSeconds) {
		_scansTotal.Add(1, new KeyValuePair<string, object?>("status", "failed"));
		_scanDuration.Record(durationSeconds);
	}

	public void RecordDuplicatesFound(int count) => _duplicatesFound.Add(count);

	public void RecordFilesProcessed(int count) => _filesProcessedTotal.Add(count);

	public void RecordApiRequest(string endpoint, int statusCode) {
		_apiRequestsTotal.Add(1,
			new KeyValuePair<string, object?>("endpoint", endpoint),
			new KeyValuePair<string, object?>("status_code", statusCode));
	}

	public void RecordWebhookDispatch(string webhookId, bool success) {
		_webhookDispatchesTotal.Add(1,
			new KeyValuePair<string, object?>("webhook_id", webhookId),
			new KeyValuePair<string, object?>("success", success));
	}
}
