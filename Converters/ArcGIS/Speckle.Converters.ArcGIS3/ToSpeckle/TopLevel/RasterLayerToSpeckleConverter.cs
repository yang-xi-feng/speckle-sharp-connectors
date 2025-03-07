using ArcGIS.Core.Data.Raster;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;
using RasterLayer = ArcGIS.Desktop.Mapping.RasterLayer;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(RasterLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RasterLayerToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<RasterLayer, SGIS.RasterLayer>
{
  private readonly ITypedConverter<Raster, RasterElement> _gisRasterConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public RasterLayerToSpeckleConverter(
    ITypedConverter<Raster, RasterElement> gisRasterConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _gisRasterConverter = gisRasterConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {
    return Convert((RasterLayer)target);
  }

  public SGIS.RasterLayer Convert(RasterLayer target)
  {
    var speckleLayer = new SGIS.RasterLayer();

    // layer native crs (for writing properties e.g. resolution, origin etc.)
    var spatialRefRaster = target.GetSpatialReference();
    // get active map CRS if layer CRS is empty
    if (spatialRefRaster.Unit is null)
    {
      spatialRefRaster = _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference;
    }
    speckleLayer.rasterCrs = new CRS
    {
      wkt = spatialRefRaster.Wkt,
      name = spatialRefRaster.Name,
      units_native = spatialRefRaster.Unit.ToString(),
    };

    // write details about the Raster
    RasterElement element = _gisRasterConverter.Convert(target.GetRaster());
    element.applicationId = $"{target.URI}_0";
    speckleLayer.elements.Add(element);

    return speckleLayer;
  }
}
