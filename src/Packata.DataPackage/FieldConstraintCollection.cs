using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.Core;
public class FieldConstraintCollection
{
    private readonly List<Constraint> _constraints = new();
    public void Add(Constraint constraint)
    {
        if (Any(constraint.GetType()))
            throw new DuplicatedConstraintException(constraint);
        _constraints.Add(constraint);
    }

    public bool Any<T>() where T : Constraint
        => _constraints.OfType<T>().Any();

    public bool Any(Type constraintType)
        => _constraints.Any(ctr => ctr.GetType() == constraintType);

    public void AddRange(IEnumerable<Constraint> constraints)
    {
        foreach (var constraint in constraints)
            Add(constraint);
    }

    public void Clear()
        => _constraints.Clear();

    public bool Remove(Constraint constraint)
        => _constraints.Remove(constraint);

    public bool Remove<T>() where T : Constraint
    {
        var constraint = _constraints.OfType<T>().FirstOrDefault();
        return constraint is not null && Remove(constraint);
    }

    public T? Get<T>() where T : Constraint
    {
        var constraint = _constraints.OfType<T>().FirstOrDefault();
        return constraint;
    }

    public T[] TypeOf<T>() where T : Constraint
    {
        var constraints = _constraints.OfType<T>();
        return [..constraints];
    }

    public int Count => _constraints.Count;
    public IEnumerator<Constraint> GetEnumerator()
        => _constraints.GetEnumerator();
    public Constraint this[int index]
    {
        get => _constraints[index];
        set => _constraints[index] = value;
    }
}
