using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification.ServerTypes;
public class AzureServer : BaseServer
{
    /// <summary>
    /// Fully qualified path to Azure Blob Storage or Azure Data Lake Storage (ADLS), supports globs. Starting with az:// or abfss
    /// </summary>
    /// <example>
    /// az://my_storage_account_name.blob.core.windows.net/my_container/path/*.parquet
    /// abfss://my_storage_account_name.dfs.core.windows.net/my_container_name/path/*.parquet
    /// </example>
    public required string Location { get; set; }

    /// <summary>
    /// The name of the bucket.
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// (Only for format = json), how multiple json documents are delimited within one file, e.g., new_line, array
    /// </summary>
    public string? Delimiter { get; set; }
}
