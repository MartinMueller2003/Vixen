﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls.WpfPropertyGrid;
using Catel.Collections;
using Common.WPFCommon.ViewModel;
using VixenModules.App.CustomPropEditor.Services;

namespace VixenModules.App.CustomPropEditor.Model
{
    /// <summary>
    /// Symbolic of an ElementNode in Vixen Core
    /// </summary>
	public class ElementModel : BindableBase, IDataErrorInfo, IEqualityComparer<ElementModel>, IEquatable<ElementModel>
	{
        private const int DefaultLightSize = 3;
	    private ObservableCollection<Light> _lights;
	    private ObservableCollection<ElementModel> _children;
	    private ObservableCollection<ElementModel> _parents;
        private int _order;
	    private string _name;
	    private int _lightSize;
	    
		#region Constructors

		public ElementModel()
		{
			Lights = new ObservableCollection<Light>();
			Children = new ObservableCollection<ElementModel>();
			Parents = new ObservableCollection<ElementModel>();
			Id = Guid.NewGuid();
			LightSize = DefaultLightSize;
		}

		public ElementModel(string name) : this()
		{
			Name = name;
		}

		public ElementModel(string name, int order) : this(name)
		{
			Name = name;
			Order = order;
		}

		public ElementModel(string name, ElementModel parent) : this(name)
		{
			Parents.Add(parent);
		}

		public ElementModel(string name, int order, ElementModel parent) : this(name)
		{
			Parents.Add(parent);
			Order = order;
		}

		#endregion

		#region Id

		public Guid Id { get; protected set; }

		#endregion

		#region Name

		public string Name
		{
			get { return _name; }
			set
			{
				if (value == _name) return;
				_name = value;
				OnPropertyChanged(nameof(Name));
			}
		}

		#endregion

		#region Order

		public int Order
	    {
	        get
	        {
	            if (IsLeaf)
	            {
	                return _order;
	            }

	            return -1;
	        }
	        set
	        {
	            if (value == _order) return;
	            _order = value;
	            OnPropertyChanged(nameof(Order));
	        }
	    }

		#endregion Order

		#region IsString

		public bool IsString => _lights.Count > 1;

		#endregion

		#region Light Count

		public int LightCount => _lights.Count;

		#endregion

		#region Light Size

		public int LightSize
		{
			get { return _lightSize; }
			set
			{
				if (value == _lightSize) return;
				_lightSize = value;
				UpdateLightSize();
				OnPropertyChanged(nameof(LightSize));
			}
		}

		#endregion

		#region IsLeaf

		[Browsable(false)]
		public bool IsLeaf => !Children.Any();

		#endregion

		#region Children

		public ObservableCollection<ElementModel> Children
		{
			get { return _children; }
			set
			{
				if (Equals(value, _children)) return;
				_children = value;
				OnPropertyChanged(nameof(Children));
			}
		}

		#endregion

		#region Parents

		public ObservableCollection<ElementModel> Parents
		{
			get { return _parents; }
			set
			{
				if (Equals(value, _parents)) return;
				_parents = value;
				OnPropertyChanged(nameof(Parents));
			}
		}

		#endregion

		#region Lights

		public ObservableCollection<Light> Lights
		{
			get { return _lights; }
			set
			{
				if (Equals(value, _lights)) return;
				_lights = value;
				OnPropertyChanged(nameof(Lights));
				OnPropertyChanged(nameof(IsString));
				OnPropertyChanged(nameof(LightCount));
			}
		}

		#endregion

		public bool RemoveParent(ElementModel parent)
	    {
	        bool success = Parents.Remove(parent);
		    parent.RemoveChild(this);
		    if (!Parents.Any())
		    {
				//We are now orphaned and need to clean up
			    if (IsLeaf)
			    {
					//We are at the bottom and just need to remove our lights
					Lights.Clear();
				}
			    else
			    {
				    foreach (var child in Children)
				    {
					    child.RemoveParent(this);
				    }
			    }
		    }

		    return success;
	    }

	    public void AddParent(ElementModel parent)
	    {
	        Parents.Add(parent);
	    }

	    public void AddChild(ElementModel em)
	    {
            Children.Add(em);
	    }

	    public bool RemoveChild(ElementModel child)
	    {
	        return Children.Remove(child);
	    }

	    internal void AddLight(Light ln)
	    {
	        if (!IsLeaf)
	        {
	            throw new ArgumentException("Non leaf model cannot have lights!");
	        }
            Lights.Add(ln);
	        ln.ParentModelId = Id;
	        OnPropertyChanged(nameof(IsString));
	        OnPropertyChanged(nameof(LightCount));
        }

	    public bool RemoveLight(Light light)
	    {
	        var success = Lights.Remove(light);
            light.ParentModelId = Guid.Empty;
	        OnPropertyChanged(nameof(IsString));
	        OnPropertyChanged(nameof(LightCount));
	        return success;
	    }

	    
	    private void UpdateLightSize()
	    {
	        Lights.ForEach(x => x.Size = LightSize);
	    }

	    public IEnumerable<ElementModel> GetLeafEnumerator()
	    {
	        if (IsLeaf)
            { 
	            return (new[] { this });
	        }

	        return Children.SelectMany(x => x.GetLeafEnumerator());
	    }

	    public IEnumerable<ElementModel> GetChildEnumerator()
	    {
	        return Children.SelectMany(x => x.GetChildEnumerator());
	    }

        public bool Equals(ElementModel x, ElementModel y)
		{
			return y != null && x != null && x.Id == y.Id;
		}

		public int GetHashCode(ElementModel obj)
		{
			return obj.Id.GetHashCode();
		}

		public bool Equals(ElementModel other)
		{
			return other != null && Id == other.Id;
		}

		public override int GetHashCode()
		{
			return Id.GetHashCode();
		}

	    public string this[string columnName]
	    {
	        get
	        {
	            string result = string.Empty;
	            if (columnName == nameof(Name))
	            {
	                if (string.IsNullOrEmpty(Name))
	                {
	                    result = "Name can not be empty";
                    }
	                else if(PropModelServices.Instance().IsNameDuplicated(Name))
	                {
	                    result = "Duplicate name";
	                }

                }
	            else if(columnName == nameof(LightSize))
	            {
	                if (LightSize <= 0)
	                {
	                    result = "Light size must be > 0";
	                }
	            }
	            return result;
	        }
        }

        [Browsable(false)]
	    public string Error {
            get { return string.Empty; }
        }
	}
}
