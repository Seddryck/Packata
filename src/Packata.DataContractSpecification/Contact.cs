using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Packata.DataContractSpecification;

/// <summary>
/// Contact information for the data contract.
/// </summary>
public class Contact
{
    /// <summary>
    /// The identifying name of the contact person or organization.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The URL pointing to contact information. Must be a valid URI.
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// The email address of the contact person or organization. Must be a valid email.
    /// </summary>
    public string Email { get; set; }
}
