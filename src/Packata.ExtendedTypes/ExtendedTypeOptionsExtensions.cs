using Packata.ResourceReaders;

namespace Packata.ExtendedTypes;

/// <summary>Enables optional representations on a Packata reader factory.</summary>
public static class ExtendedTypeOptionsExtensions
{
    /// <summary>Exposes supported spatial fields as NetTopologySuite geometry values.</summary>
    public static ResourceReaderFactoryOptions UseNetTopologySuite(this ResourceReaderFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddRepresentationProfile(new("NetTopologySuite",
            NetTopologySuiteProfile.SelectType, NetTopologySuiteDelimitedBinding.Binding));
    }

    /// <summary>Exposes year and year-month fields as Chrononuensis values.</summary>
    public static ResourceReaderFactoryOptions UseChrononuensis(this ResourceReaderFactoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddRepresentationProfile(new("Chrononuensis",
            ChrononuensisProfile.SelectType, ChrononuensisDelimitedBinding.Binding));
    }
}
