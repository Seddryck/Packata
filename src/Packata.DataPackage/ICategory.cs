using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public interface ICategory
{
    string Label { get; }
}

public class Category : ICategory
{
    public string Label { get; }
    public int Value { get; }
    public Category(int value, string label)
        => (Value, Label) = (value, label);
}

public class CategoryLabel : ICategory
{
    public string Label { get; }
    public CategoryLabel(string label)
        => (Label) = (label);
}
