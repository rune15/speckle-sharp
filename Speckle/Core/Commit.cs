﻿using System;
using System.Collections.Generic;
using System.Linq;
using Speckle.Models;

namespace Speckle.Core
{
  public class Commit : Base
  {
    [DetachProperty]
    public List<Base> Objects { get; set; } = new List<Base>();

    [ExcludeHashing]
    public string Name { get; set; }

    [ExcludeHashing]
    public string Description { get; set; }

    [ExcludeHashing] // dubious: should we really exclude the parents from hashing? 
    public HashSet<string> Parents { get; set; } = new HashSet<string>();

    [ExcludeHashing]
    public User Author { get; set; }

    //[ExcludeHashing]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    public Commit() { }
  }

  /// <summary>
  /// Class used to shallowly deserialize a commit.
  /// </summary>
  public class ShallowCommit : Commit
  {
    public override string hash { get; set; }

    public new List<ObjectReference> Objects { get; set; } = new List<ObjectReference>();

    public List<string> __tree { get; set; }

    public ShallowCommit() { }

    /// <summary>
    /// Returns a flattened list of all objects in this commit, including nested ones.
    /// </summary>
    /// <returns></returns>
    public HashSet<string> GetAllObjects()
    {
      var objs = new HashSet<string>();

      foreach(string str in __tree)
      {
        var items = str.Split('.');
        for(int i = 1; i < items.Count(); i++) // Skip first item as that is always the commit itself.
        {
          objs.Add(items[i]);
        }
      }

      return objs;
    }
  }
}