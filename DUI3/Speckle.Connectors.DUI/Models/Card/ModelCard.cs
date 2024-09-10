using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.DUI.Utils;

namespace Speckle.Connectors.DUI.Models.Card;

public class ModelCard : DiscriminatedObject
{
  /// <summary>
  /// This is a unique id generated by the ui to make model cards easier to reference around.
  /// It's not the actual model (branch) id.
  /// </summary>
  public string? ModelCardId { get; set; }

  /// <summary>
  /// Model id. FKA branch id.
  /// </summary>
  public string? ModelId { get; set; }

  /// <summary>
  /// Project id. FKA stream id.
  /// </summary>
  public string? ProjectId { get; set; }

  /// <summary>
  /// Account id that model card created with it initially.
  /// </summary>
  public string? AccountId { get; set; }

  /// <summary>
  /// Server that model card created on it initially.
  /// </summary>
  public string? ServerUrl { get; set; }

  public List<CardSetting>? Settings { get; set; }
}
