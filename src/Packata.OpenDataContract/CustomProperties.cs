using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.OpenDataContract;
public class CustomProperties : List<CustomProperty>
{
    public IEnumerable<string> Keys
        => this.Where(x => x.Property is not null).Select(x => x.Property!);

    public object? this[string property]
        => this.Last(x => string.Equals(x.Property, property, StringComparison.Ordinal)).Value;

    public bool TryGetValue(string property, out object? value)
    {
        var item = this.LastOrDefault(x => string.Equals(x.Property, property, StringComparison.Ordinal));
        value = item?.Value;
        return item is not null;
    }
}
