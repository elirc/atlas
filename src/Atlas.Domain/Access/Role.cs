namespace Atlas.Domain.Access;

/// <summary>What a user may do inside ONE org. Platform staff use User.IsAtlasOps instead.</summary>
// LEARN: org roles and platform roles are different axes, so they are different
// types. Folding "AtlasOps" into this enum would force every org-scoped authz check
// to special-case a role that has no org.
public enum Role
{
    OrgAdmin = 1,
    HrManager = 2,
    Employee = 3,
    Contractor = 4,
}
