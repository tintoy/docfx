using System.Collections.Concurrent;
using System.Threading;

namespace Microsoft.DocAsCode.Common
{
    /// <summary>
    ///     A NETStandard-friendly replacement for CallContext.
    /// </summary>
    /// <remarks>
    ///     Adapted from <see href="http://www.cazzulino.com/callcontext-netstandard-netcore.html"/>.
    /// </remarks>
    public static class LogicalCallContext
    {
        /// <summary>
        ///     Data associated with the logical call context.
        /// </summary>
        static readonly ConcurrentDictionary<string, AsyncLocal<object>> _data = new ConcurrentDictionary<string, AsyncLocal<object>>();

        /// <summary>
        ///     Add or update data in the logical call context.
        /// </summary>
        /// <param name="key">A key that uniquely identifies the data.</param>
        /// <param name="data">The data.</param>
        public static void SetData(string key, object data) => _data.GetOrAdd(key, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        ///     Get data from the logical call context.
        /// </summary>
        /// <param name="key">A key that uniquely identifies the data.</param>
        /// <returns>The data, or <c>null</c> if no data is present with the specified key.</returns>
        public static object GetData(string key) => _data.TryGetValue(key, out AsyncLocal<object> data) ? data.Value : null;

        /// <summary>
        ///     Remove data from the logical call context.
        /// </summary>
        /// <param name="key">A key that uniquely identifies the data.</param>
        public static void FreeData(string key) => _data.TryRemove(key, out _);
    }
}
