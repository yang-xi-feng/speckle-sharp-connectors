using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Class that unpacks a given set of selection elements into atomic objects.
/// </summary>
public class ElementUnpacker
{
  private readonly RevitContext _revitContext;

  public ElementUnpacker(RevitContext revitContext)
  {
    _revitContext = revitContext;
  }

  /// <summary>
  /// Unpacks a random set of revit objects into atomic objects. It currently unpacks groups recurisvely, nested families into atomic family instances.
  /// This method will also "pack" curtain walls if necessary (ie, if mullions or panels are selected without their parent curtain wall, they are sent independently; if the parent curtain wall is selected, they will be removed out as the curtain wall will include all its children).
  /// </summary>
  /// <param name="selectionElements"></param>
  /// <returns></returns>
  public IEnumerable<Element> UnpackSelectionForConversion(IEnumerable<Element> selectionElements)
  {
    // Note: steps kept separate on purpose.
    // Step 1: unpack groups
    var atomicObjects = UnpackElements(selectionElements);

    // Step 2: pack curtain wall elements, once we know the full extent of our flattened item list.
    // The behaviour we're looking for:
    // If parent wall is part of selection, does not select individual elements out. Otherwise, selects individual elements (Panels, Mullions) as atomic objects.
    return PackCurtainWallElements(atomicObjects);
  }

  /// <summary>
  /// Unpacks input element ids into their subelements, eg groups and nested family instances
  /// </summary>
  /// <param name="objectIds"></param>
  /// <returns></returns>
  /// <remarks>
  /// This is used to invalidate object ids in the send conversion cache when the selected object id is only the parent element id
  /// </remarks>
  public IEnumerable<string> GetUnpackedElementIds(List<string> objectIds)
  {
    var doc = _revitContext.UIApplication?.ActiveUIDocument.Document!;
    var docElements = doc.GetElements(objectIds);
    return UnpackSelectionForConversion(docElements).Select(o => o.UniqueId).ToList();
  }

  private List<Element> UnpackElements(IEnumerable<Element> elements)
  {
    var unpackedElements = new List<Element>(); // note: could be a hashset/map so we prevent duplicates (?)
    var doc = _revitContext.UIApplication?.ActiveUIDocument.Document!;

    foreach (var element in elements)
    {
      // UNPACK: Groups
      if (element is Group g)
      {
        // POC: this might screw up generating hosting rel generation here, because nested families in groups get flattened out by GetMemberIds().
        var groupElements = g.GetMemberIds().Select(doc.GetElement);
        unpackedElements.AddRange(UnpackElements(groupElements));
      }
      // UNPACK: Family instances (as they potentially have nested families inside)
      else if (element is FamilyInstance familyInstance)
      {
        var familyElements = familyInstance.GetSubComponentIds().Select(doc.GetElement).ToArray();

        if (familyElements.Length != 0)
        {
          unpackedElements.AddRange(UnpackElements(familyElements));
        }

        unpackedElements.Add(familyInstance);
      }
      else if (element is MultistoryStairs multistoryStairs)
      {
        var stairs = multistoryStairs.GetAllStairsIds().Select(doc.GetElement);
        unpackedElements.AddRange(UnpackElements(stairs));
      }
      else
      {
        unpackedElements.Add(element);
      }
    }
    // Why filtering for duplicates? Well, well, well... it's related to the comment above on groups: if a group
    // contains a nested family, GetMemberIds() will return... duplicates of the exploded family components.
    return unpackedElements.GroupBy(el => el.Id).Select(g => g.First()).ToList(); // no disinctBy in here sadly.
  }

  private List<Element> PackCurtainWallElements(List<Element> elements)
  {
    var ids = elements.Select(el => el.Id).ToArray();
    var doc = _revitContext.UIApplication?.ActiveUIDocument.Document!;
    elements.RemoveAll(element =>
      (element is Mullion m && ids.Contains(m.Host.Id))
      || (element is Panel p && ids.Contains(p.Host.Id))
      || (
        element is FamilyInstance { Host: not null } f
        && doc.GetElement(f.Host.Id) is Wall { CurtainGrid: not null }
        && ids.Contains(f.Host.Id)
      )
    );
    return elements;
  }

  /// <summary>
  /// Given a set of atomic elements, it will return a list of all their ids as well as their subelements. This currently handles <b>curtain walls</b> and <b>stacked walls</b>.
  /// This might not be an exhaustive list of valid objects with "subelements" in revit, and will need revisiting.
  /// </summary>
  /// <param name="elements"></param>
  /// <returns></returns>
  public List<string> GetElementsAndSubelementIdsFromAtomicObjects(List<Element> elements)
  {
    var ids = new HashSet<string>();
    foreach (var element in elements)
    {
      switch (element)
      {
        case Wall wall:
          if (wall.CurtainGrid is { } grid)
          {
            foreach (var mullionId in grid.GetMullionIds())
            {
              ids.Add(mullionId.ToString());
            }
            foreach (var panelId in grid.GetPanelIds())
            {
              ids.Add(panelId.ToString());
            }
          }
          else if (wall.IsStackedWall)
          {
            foreach (var stackedWallId in wall.GetStackedWallMemberIds())
            {
              ids.Add(stackedWallId.ToString());
            }
          }
          break;
        case FootPrintRoof footPrintRoof:
          if (footPrintRoof.CurtainGrids is { } gs)
          {
            foreach (CurtainGrid roofGrid in gs)
            {
              foreach (var mullionId in roofGrid.GetMullionIds())
              {
                ids.Add(mullionId.ToString());
              }
              foreach (var panelId in roofGrid.GetPanelIds())
              {
                ids.Add(panelId.ToString());
              }
            }
          }
          break;
        default:
          break;
      }

      ids.Add(element.Id.ToString());
    }

    return ids.ToList();
  }
}
