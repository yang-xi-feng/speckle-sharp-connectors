using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Utils.Caching;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Operations.Send.Settings;

public class ToSpeckleSettingsManager
{
  private readonly RevitContext _revitContext;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;

  // cache invalidation process run with ModelCardId since the settings are model specific
  private readonly Dictionary<string, DetailLevelType> _detailLevelCache = new();
  private readonly Dictionary<string, Transform?> _referencePointCache = new();

  public ToSpeckleSettingsManager(
    RevitContext revitContext,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker
  )
  {
    _revitContext = revitContext;
    _elementUnpacker = elementUnpacker;
    _sendConversionCache = sendConversionCache;
  }

  public ToSpeckleSettings GetToSpeckleSettings(SenderModelCard modelCard)
  {
    DetailLevelType detailLevel = GetDetailLevelSetting(modelCard);
    Transform? referencePointTransform = GetReferencePointSetting(modelCard);

    return new ToSpeckleSettings(detailLevel, referencePointTransform);
  }

  private DetailLevelType GetDetailLevelSetting(SenderModelCard modelCard)
  {
    var fidelityString = modelCard.Settings?.First(s => s.Id == "detailLevel").Value as string;
    if (
      fidelityString is not null
      && DetailLevelSetting.GeometryFidelityMap.TryGetValue(fidelityString, out DetailLevelType fidelity)
    )
    {
      if (_detailLevelCache.TryGetValue(modelCard.ModelCardId.NotNull(), out DetailLevelType previousType))
      {
        if (previousType != fidelity)
        {
          var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.GetObjectIds() : [];
          var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds);
          _sendConversionCache.EvictObjects(unpackedObjectIds);
        }
      }
      _detailLevelCache[modelCard.ModelCardId.NotNull()] = fidelity;
      return fidelity;
    }

    throw new ArgumentException($"Invalid geometry fidelity value: {fidelityString}");
  }

  private Transform? GetReferencePointSetting(SenderModelCard modelCard)
  {
    var referencePointString = modelCard.Settings?.First(s => s.Id == "referencePoint").Value as string;
    if (
      referencePointString is not null
      && ReferencePointSetting.ReferencePointMap.TryGetValue(
        referencePointString,
        out ReferencePointType referencePoint
      )
    )
    {
      // get the current transform from setting first
      // we are doing this because we can't track if reference points were changed between send operations.
      Transform? currentTransform = GetTransform(_revitContext, referencePoint);

      if (_referencePointCache.TryGetValue(modelCard.ModelCardId.NotNull(), out Transform? previousTransform))
      {
        // invalidate conversion cache if the transform has changed
        if (previousTransform != currentTransform)
        {
          var objectIds = modelCard.SendFilter != null ? modelCard.SendFilter.GetObjectIds() : [];
          var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds);
          _sendConversionCache.EvictObjects(unpackedObjectIds);
        }
      }

      _referencePointCache[modelCard.ModelCardId.NotNull()] = currentTransform;
      return currentTransform;
    }

    throw new ArgumentException($"Invalid reference point value: {referencePointString}");
  }

  private Transform? GetTransform(RevitContext context, ReferencePointType referencePointType)
  {
    Transform? referencePointTransform = null;

    if (context.UIApplication is UIApplication uiApplication)
    {
      // first get the main doc base points and reference setting transform
      using FilteredElementCollector filteredElementCollector = new(uiApplication.ActiveUIDocument.Document);
      var points = filteredElementCollector.OfClass(typeof(BasePoint)).Cast<BasePoint>().ToList();
      BasePoint? projectPoint = points.FirstOrDefault(o => !o.IsShared);
      BasePoint? surveyPoint = points.FirstOrDefault(o => o.IsShared);

      switch (referencePointType)
      {
        // note that the project base (ui) rotation is registered on the survey pt, not on the base point
        case ReferencePointType.ProjectBase:
          if (projectPoint is not null)
          {
            referencePointTransform = Transform.CreateTranslation(projectPoint.Position);
          }
          else
          {
            throw new InvalidOperationException("Couldn't retrieve Project Point from document");
          }
          break;

        // note that the project base (ui) rotation is registered on the survey pt, not on the base point
        case ReferencePointType.Survey:
          if (surveyPoint is not null && projectPoint is not null)
          {
            // POC: should a null angle resolve to 0?
            // retrieve the survey point rotation from the project point
            var angle = projectPoint.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM)?.AsDouble() ?? 0;

            // POC: following disposed incorrectly or early or maybe a false negative?
            using Transform translation = Transform.CreateTranslation(surveyPoint.Position);
            referencePointTransform = translation.Multiply(Transform.CreateRotation(XYZ.BasisZ, angle));
          }
          else
          {
            throw new InvalidOperationException("Couldn't retrieve Survey and Project Point from document");
          }
          break;

        case ReferencePointType.InternalOrigin:
          break;

        default:
          break;
      }

      return referencePointTransform;
    }

    throw new InvalidOperationException(
      "Revit Context UI Application was null when retrieving reference point transform."
    );
  }
}
