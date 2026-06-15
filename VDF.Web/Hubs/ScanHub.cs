using Microsoft.AspNetCore.SignalR;
using VDF.Web.Models;
using VDF.Web.Services;

namespace VDF.Web.Hubs;

/// <summary>
/// SignalR hub for real-time scan progress notifications.
/// Server pushes events to clients; no client-to-server methods are needed
/// (all actions go through the REST API).
/// </summary>
public sealed class ScanHub : Hub {
	// Server-to-client methods (invoked via IHubContext<ScanHub>):
	//   ProgressUpdate(ScanProgressResponse payload)
	//   StateChanged(string state)
	//   FileOpProgress(int current, int max, string verb)
	//
	// No client-to-server methods are defined.
}
