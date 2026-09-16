using System;

namespace ECS.Viewer.Host {
	public sealed class ViewerHttpResult {
		public int StatusCode { get; }
		public string ContentType { get; }
		public byte[] Body { get; }

		public ViewerHttpResult(int statusCode, string contentType, byte[] body) {
			StatusCode = statusCode;
			ContentType = contentType;
			Body = body ?? Array.Empty<byte>();
		}
	}
}
