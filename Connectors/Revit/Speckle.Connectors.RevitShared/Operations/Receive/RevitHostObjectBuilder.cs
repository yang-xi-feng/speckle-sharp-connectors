using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Revit.Async;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Revit.Operations.Receive;

internal sealed class RevitHostObjectBuilder : IHostObjectBuilder, IDisposable
{
  private readonly IRootToHostConverter _converter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _revitToHostCacheSingleton;
  private readonly ITransactionManager _transactionManager;
  private readonly ILocalToGlobalUnpacker _localToGlobalUnpacker;
  private readonly LocalToGlobalConverterUtils _localToGlobalConverterUtils;
  private readonly RevitGroupBaker _groupBaker;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly ILogger<RevitHostObjectBuilder> _logger;

  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly ISdkActivityFactory _activityFactory;

  public RevitHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITransactionManager transactionManager,
    ISdkActivityFactory activityFactory,
    ILocalToGlobalUnpacker localToGlobalUnpacker,
    LocalToGlobalConverterUtils localToGlobalConverterUtils,
    RevitGroupBaker groupManager,
    RevitMaterialBaker materialBaker,
    RootObjectUnpacker rootObjectUnpacker,
    ILogger<RevitHostObjectBuilder> logger,
    RevitToHostCacheSingleton revitToHostCacheSingleton
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _transactionManager = transactionManager;
    _localToGlobalUnpacker = localToGlobalUnpacker;
    _localToGlobalConverterUtils = localToGlobalConverterUtils;
    _groupBaker = groupManager;
    _materialBaker = materialBaker;
    _rootObjectUnpacker = rootObjectUnpacker;
    _logger = logger;
    _revitToHostCacheSingleton = revitToHostCacheSingleton;
    _activityFactory = activityFactory;
  }

  public Task<HostObjectBuilderResult> Build(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  ) =>
    RevitTask.RunAsync(() => BuildSync(rootObject, projectName, modelName, onOperationProgressed, cancellationToken));

  private HostObjectBuilderResult BuildSync(
    Base rootObject,
    string projectName,
    string modelName,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var baseGroupName = $"Project {projectName}: Model {modelName}"; // TODO: unify this across connectors!

    onOperationProgressed?.Invoke("Converting", null);
    using var activity = _activityFactory.Start("Build");

    // 0 - Clean then Rock n Roll! 🎸
    using TransactionGroup preReceiveCleanTransaction = new(_converterSettings.Current.Document, "Pre-receive clean");
    preReceiveCleanTransaction.Start();
    _transactionManager.StartTransaction(true);

    try
    {
      PreReceiveDeepClean(baseGroupName);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to clean up before receive in Revit");
    }

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      preReceiveCleanTransaction.Assimilate();
    }

    // 1 - Unpack objects and proxies from root commit object
    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);
    var localToGlobalMaps = _localToGlobalUnpacker.Unpack(
      unpackedRoot.DefinitionProxies,
      unpackedRoot.ObjectsToConvert.ToList()
    );

    using TransactionGroup transactionGroup =
      new(_converterSettings.Current.Document, $"Received data from {projectName}");
    transactionGroup.Start();
    _transactionManager.StartTransaction();

    if (unpackedRoot.RenderMaterialProxies != null)
    {
      _materialBaker.MapLayersRenderMaterials(unpackedRoot);
      // NOTE: do not set _contextStack.RenderMaterialProxyCache directly, things stop working. Ogu/Dim do not know why :) not a problem as we hopefully will refactor some of these hacks out.
      var map = _materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseGroupName);
      foreach (var kvp in map)
      {
        _revitToHostCacheSingleton.MaterialsByObjectId.Add(kvp.Key, kvp.Value);
      }
    }

    var conversionResults = BakeObjects(localToGlobalMaps, onOperationProgressed, cancellationToken);

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      transactionGroup.Assimilate();
    }
    
    using TransactionGroup createGroupTransaction = new(_converterSettings.Current.Document, "Creating group");
    createGroupTransaction.Start();
    _transactionManager.StartTransaction(true);

    foreach (var (res, applicationId) in conversionResults.Item2)
    {
      var elGeometry = res.get_Geometry(new Options() { DetailLevel = ViewDetailLevel.Undefined });
      var materialId = ElementId.InvalidElementId;
      if (
        _revitToHostCacheSingleton.MaterialsByObjectId.TryGetValue(applicationId, out var mappedElementId)
      )
      {
        materialId = mappedElementId;
      }
      // NOTE: some geometries fail to convert as solids, and the api defaults back to meshes (from the shape importer). These cannot be painted, so don't bother.
      foreach (var geo in elGeometry)
      {
        if (geo is Solid s)
        {
          foreach (Face face in s.Faces)
          {
            _converterSettings.Current.Document.Paint(res.Id, face, materialId);
          }
        }
      }
    }
    
    try
    {
      _groupBaker.BakeGroups(baseGroupName);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to create group after receiving elements in Revit");
    }

    using (var _ = _activityFactory.Start("Commit"))
    {
      _transactionManager.CommitTransaction();
      createGroupTransaction.Assimilate();
    }

    _revitToHostCacheSingleton.MaterialsByObjectId.Clear(); // Massive hack!

    return conversionResults.Item1;
  }

  private (HostObjectBuilderResult, List<(DirectShape res, string applicationId)>) BakeObjects(
    List<LocalToGlobalMap> localToGlobalMaps,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var _ = _activityFactory.Start("BakeObjects");
    var conversionResults = new List<ReceiveConversionResult>();
    var bakedObjectIds = new List<string>();
    int count = 0;
    
    var toPaintLater = new List<(DirectShape res, string applicationId)>();
    
    foreach (LocalToGlobalMap localToGlobalMap in localToGlobalMaps)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        using var activity = _activityFactory.Start("BakeObject");
        var atomicObject = _localToGlobalConverterUtils.TransformObjects(
          localToGlobalMap.AtomicObject,
          localToGlobalMap.Matrix
        );
        var result = _converter.Convert(atomicObject);
        onOperationProgressed?.Invoke("Converting", (double)++count / localToGlobalMaps.Count);

        // Note: our current converter always returns a DS for now
        if (result is DirectShape ds)
        {
          bakedObjectIds.Add(ds.UniqueId.ToString());
          _groupBaker.AddToGroupMapping(localToGlobalMap.TraversalContext, ds);
          if (atomicObject is IRawEncodedObject && atomicObject is Base myBase)
          {
            toPaintLater.Add((ds, myBase.applicationId ?? myBase.id));
          } 
        }
        else
        {
          throw new SpeckleConversionException($"Failed to cast {result.GetType()} to Direct Shape.");
        }
        conversionResults.Add(new(Status.SUCCESS, atomicObject, ds.UniqueId, "Direct Shape"));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, localToGlobalMap.AtomicObject, null, null, ex));
      }
    }
    return (new(bakedObjectIds, conversionResults), toPaintLater);
  }

  private void PreReceiveDeepClean(string baseGroupName)
  {
    _groupBaker.PurgeGroups(baseGroupName);
    _materialBaker.PurgeMaterials(baseGroupName);
  }

  public void Dispose()
  {
    _transactionManager?.Dispose();
  }
}
