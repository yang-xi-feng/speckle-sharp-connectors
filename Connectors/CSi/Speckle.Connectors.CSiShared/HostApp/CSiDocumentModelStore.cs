﻿using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Helpers;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.CSiShared.HostApp;

public class CSiDocumentModelStore : DocumentModelStore
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<CSiDocumentModelStore> _logger;
  private readonly ICSiApplicationService _csiApplicationService;
  private string HostAppUserDataPath { get; set; }
  private string DocumentStateFile { get; set; }
  private string ModelPathHash { get; set; }

  public CSiDocumentModelStore(
    IJsonSerializer jsonSerializerSettings,
    ISpeckleApplication speckleApplication,
    ILogger<CSiDocumentModelStore> logger,
    ICSiApplicationService csiApplicationService
  )
    : base(jsonSerializerSettings)
  {
    _speckleApplication = speckleApplication;
    _logger = logger;
    _csiApplicationService = csiApplicationService;
    SetPaths();
    LoadState();
  }

  private void SetPaths()
  {
    ModelPathHash = Crypt.Md5(_csiApplicationService.SapModel.GetModelFilepath(), length: 32);
    HostAppUserDataPath = Path.Combine(
      SpecklePathProvider.UserSpeckleFolderPath,
      "ConnectorsFileData",
      _speckleApplication.Slug
    );
    DocumentStateFile = Path.Combine(HostAppUserDataPath, $"{ModelPathHash}.json");
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    try
    {
      if (!Directory.Exists(HostAppUserDataPath))
      {
        Directory.CreateDirectory(HostAppUserDataPath);
      }
      File.WriteAllText(DocumentStateFile, modelCardState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex.Message);
    }
  }

  protected override void LoadState()
  {
    if (!Directory.Exists(HostAppUserDataPath))
    {
      ClearAndSave();
      return;
    }

    if (!File.Exists(DocumentStateFile))
    {
      ClearAndSave();
      return;
    }

    string serializedState = File.ReadAllText(DocumentStateFile);
    LoadFromString(serializedState);
  }
}
