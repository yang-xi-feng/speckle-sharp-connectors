using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts class properties deemed important from a civil entity.
/// Should not repeat any data that would be included on property sets and general properties on the object.
/// Expects to be scoped per operation.
/// </summary>
public class ClassPropertiesExtractor
{
  private readonly ITypedConverter<AG.Point3dCollection, SOG.Polyline> _point3dCollectionConverter;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly CatchmentGroupHandler _catchmentGroupHandler;
  private readonly PipeNetworkHandler _pipeNetworkHandler;

  public ClassPropertiesExtractor(
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> point3dCollectionConverter,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    CatchmentGroupHandler catchmentGroupHandler,
    PipeNetworkHandler pipeNetworkHandler
  )
  {
    _point3dCollectionConverter = point3dCollectionConverter;
    _pointConverter = pointConverter;
    _catchmentGroupHandler = catchmentGroupHandler;
    _pipeNetworkHandler = pipeNetworkHandler;
  }

  /// <summary>
  /// Extracts general properties from a civil entity. Expects to be scoped per operation.
  /// </summary>
  /// <param name="entity"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetClassProperties(CDB.Entity entity)
  {
    switch (entity)
    {
      case CDB.Catchment catchment:
        return ExtractCatchmentProperties(catchment);
      case CDB.Site site:
        return ExtractSiteProperties(site);
      case CDB.Parcel parcel:
        return ExtractParcelProperties(parcel);

      // pipe networks
      case CDB.Pipe pipe:
        return ExtractPipeProperties(pipe);
      case CDB.Structure structure:
        return ExtractStructureProperties(structure);

      // alignments
      case CDB.Alignment alignment:
        return ExtractAlignmentProperties(alignment);
      case CDB.Profile profile:
        return ExtractProfileProperties(profile);

      // assemblies
      case CDB.Subassembly subassembly:
        return ExtractSubassemblyProperties(subassembly);

      default:
        return null;
    }
  }

  private Dictionary<string, object?> ExtractParcelProperties(CDB.Parcel parcel)
  {
#if CIVIL3D2023_OR_GREATER
    return new() { ["number"] = parcel.Number, ["taxId"] = parcel.TaxId };
#else
    return new() { ["number"] = parcel.Number };
#endif
  }

  private Dictionary<string, object?> ExtractSubassemblyProperties(CDB.Subassembly subassembly)
  {
    Dictionary<string, object?> subassemblyProperties = new();

    subassemblyProperties["origin"] = _pointConverter.Convert(subassembly.Origin);

    // get shapes > links > points info
    Dictionary<string, object?> shapes = new();
    int shapeCount = 0;
    foreach (CDB.Shape shape in subassembly.Shapes)
    {
      Dictionary<string, object?> links = new();
      int linkCount = 0;
      foreach (CDB.Link link in shape.Links)
      {
        Dictionary<string, object?> points = new();
        int pointCount = 0;
        foreach (CDB.Point point in link.Points)
        {
          points[pointCount.ToString()] = new Dictionary<string, object?>()
          {
            ["elevation"] = point.Elevation,
            ["codes"] = point.Codes.ToList(),
            ["offset"] = point.Offset,
          };
          pointCount++;
        }

        links[linkCount.ToString()] = new Dictionary<string, object?>()
        {
          ["codes"] = link.Codes.ToList(),
          ["points"] = points
        };

        linkCount++;
      }

      shapes[shapeCount.ToString()] = new Dictionary<string, object?>()
      {
        ["codes"] = shape.Codes.ToList(),
        ["links"] = links
      };
    }

    subassemblyProperties["shapes"] = shapes;

    if (subassembly.HasSide)
    {
      subassemblyProperties["side"] = subassembly.Side;
    }

    return subassemblyProperties;
  }

  private Dictionary<string, object?> ExtractProfileProperties(CDB.Profile profile)
  {
    return new()
    {
      ["offset"] = profile.Offset,
      ["startingStation"] = profile.StartingStation,
      ["endingStation"] = profile.EndingStation,
      ["profileType"] = profile.ProfileType.ToString(),
      ["elevationMin"] = profile.ElevationMin,
      ["elevationMax"] = profile.ElevationMax
    };
  }

  private Dictionary<string, object?> ExtractAlignmentProperties(CDB.Alignment alignment)
  {
    Dictionary<string, object?> alignmentProperties =
      new()
      {
        ["startingStation"] = alignment.StartingStation,
        ["endingStation"] = alignment.EndingStation,
        ["alignmentType"] = alignment.AlignmentType.ToString()
      };

    if (!alignment.IsSiteless)
    {
      alignmentProperties["siteId"] = alignment.SiteId.GetSpeckleApplicationId();
    }

    return alignmentProperties;
  }

  private Dictionary<string, object?> ExtractPipeProperties(CDB.Pipe pipe)
  {
    Dictionary<string, object?> pipeProperties =
      new()
      {
        ["bearing"] = pipe.Bearing,
        ["innerDiameterOrWidth"] = pipe.InnerDiameterOrWidth,
        ["innerHeight"] = pipe.InnerHeight,
        ["slope"] = pipe.Slope,
        ["shape"] = pipe.CrossSectionalShape.ToString(),
#pragma warning disable CS0618 // Type or member is obsolete
        ["length2d"] = pipe.Length2D, //Length2D was un-obsoleted in 2023, but is still marked obsolete in 2022
#pragma warning restore CS0618 // Type or member is obsolete
        ["minimumCover"] = pipe.MinimumCover,
        ["maximumCover"] = pipe.MaximumCover,
        ["junctionLoss"] = pipe.JunctionLoss,
        ["flowDirection"] = pipe.FlowDirection.ToString(),
        ["flowRate"] = pipe.FlowRate
      };

    if (pipe.StartStructureId != ADB.ObjectId.Null)
    {
      pipeProperties["startStructureId"] = pipe.StartStructureId.GetSpeckleApplicationId();
    }

    if (pipe.EndStructureId != ADB.ObjectId.Null)
    {
      pipeProperties["endStructureId"] = pipe.EndStructureId.GetSpeckleApplicationId();
    }

    ExtractPartProperties(pipe, pipeProperties);

    return pipeProperties;
  }

  private Dictionary<string, object?> ExtractStructureProperties(CDB.Structure structure)
  {
    var location = _pointConverter.Convert(structure.Location);

    Dictionary<string, object?> structureProperties =
      new()
      {
        ["location"] = location,
        ["northing"] = structure.Northing,
        ["rotation"] = structure.Rotation,
        ["sumpDepth"] = structure.SumpDepth,
        ["sumpElevation"] = structure.SumpElevation,
        ["innerDiameterOrWidth"] = structure.InnerDiameterOrWidth
      };

    if (structure.BoundingShape == CDB.BoundingShapeType.Box)
    {
      structureProperties["innerLength"] = structure.InnerLength;
      structureProperties["length"] = structure.Length;
    }

    ExtractPartProperties(structure, structureProperties);

    return structureProperties;
  }

  private void ExtractPartProperties(CDB.Part part, Dictionary<string, object?> dict)
  {
    // process the part's pipe network with the pipe network handler
    _pipeNetworkHandler.HandlePipeNetwork(part);

    dict["domain"] = part.Domain.ToString();
    dict["partType"] = part.PartType.ToString();
    if (part.RefSurfaceId != ADB.ObjectId.Null)
    {
      dict["surfaceId"] = part.RefSurfaceId.GetSpeckleApplicationId();
    }

    return;
  }

  private Dictionary<string, object?> ExtractSiteProperties(CDB.Site site)
  {
    Dictionary<string, object?> catchmentProperties = new();

    if (site.GetAlignmentIds().Count > 0)
    {
      catchmentProperties["alignmentIds"] = GetSpeckleApplicationIdsFromCollection(site.GetAlignmentIds());
    }

    if (site.GetFeatureLineIds().Count > 0)
    {
      catchmentProperties["featureLineIds"] = GetSpeckleApplicationIdsFromCollection(site.GetFeatureLineIds());
    }

    return catchmentProperties;
  }

  private Dictionary<string, object?> ExtractCatchmentProperties(CDB.Catchment catchment)
  {
    // get the bounding curve of the catchment
    SOG.Polyline boundary = _point3dCollectionConverter.Convert(catchment.BoundaryPolyline3d);
    boundary.closed = true;

    // use the catchment group handler to process the catchment's group
    _catchmentGroupHandler.HandleCatchmentGroup(catchment);

    return new()
    {
      ["antecedentWetness"] = catchment.AntecedentWetness,
      ["area"] = catchment.Area,
      ["area2d"] = catchment.Area2d,
      ["boundary"] = boundary,
      ["hydrologicalSoilGroup"] = catchment.HydrologicalSoilGroup.ToString(),
      ["imperviousArea"] = catchment.ImperviousArea,
      ["manningsCoefficient"] = catchment.ManningsCoefficient,
      ["perimeter2d"] = catchment.Perimeter2d,
      ["timeOfConcentration"] = catchment.TimeOfConcentration
    };
  }

  private List<string> GetSpeckleApplicationIdsFromCollection(ADB.ObjectIdCollection collection)
  {
    List<string> speckleAppIds = new(collection.Count);
    foreach (ADB.ObjectId parcelId in collection)
    {
      speckleAppIds.Add(parcelId.GetSpeckleApplicationId());
    }

    return speckleAppIds;
  }
}
