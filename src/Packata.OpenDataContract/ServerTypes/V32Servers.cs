namespace Packata.OpenDataContract.ServerTypes;

public class AthenaServer : BaseServer
{
    public string? Catalog { get; set; }
    public string? Database { get; set; }
    public string? Region { get; set; }
    public string? Workgroup { get; set; }
    public string? StagingDir { get; set; }
}

public class HanaServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 30015;
    public string? Database { get; set; }
    public string? Schema { get; set; }
}

public class IcebergServer : BaseServer
{
    public required string Catalog { get; set; }
    public required string CatalogUrl { get; set; }
    public string? Namespace { get; set; }
    public string? Warehouse { get; set; }
}

public class ExasolServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 8563;
    public string? Schema { get; set; }
}

public class TeradataServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 1025;
    public string? Database { get; set; }
}

public class IngresServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 21064;
    public required string Database { get; set; }
}

public class VectorwiseServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 21064;
    public required string Database { get; set; }
}

public class VersantServer : BaseServer, IHostAware
{
    public string Host { get; set; } = "localhost";
    public object? Port { get; set; } = 5019;
    public required string Database { get; set; }
}

public class PoetServer : BaseServer, IHostAware
{
    public string Host { get; set; } = "LOCAL";
    public object? Port { get; set; } = 6001;
    public required string Database { get; set; }
}

public class ZenServer : BaseServer, IHostAware
{
    public required string Host { get; set; }
    public object? Port { get; set; } = 1583;
    public string? Database { get; set; }
}

public class GlueServer : BaseServer, IEncodingAware
{
    public string? Database { get; set; }
    public string? Region { get; set; }
    public string Encoding { get; set; } = "UTF-8";
}

public class KinesisServer : BaseServer, IEncodingAware
{
    public string? Stream { get; set; }
    public string? Region { get; set; }
    public string Encoding { get; set; } = "UTF-8";
}
