/*  This file is part of Chummer5a.
 *
 *  Chummer5a is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  Chummer5a is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with Chummer5a.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  You can obtain the full source code for Chummer5a at
 *  https://github.com/chummer5a/chummer5a
 */
using Microsoft.AspNetCore.Identity;
using System;

namespace ChummerHub.Data
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole'
    public class ApplicationRole : IdentityRole<Guid>
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole'
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.MyRole'
        public string MyRole { get; set; }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.MyRole'

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.ApplicationRole()'
        public ApplicationRole()
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.ApplicationRole()'
        {
            this.MyRole = "default";
            this.Name = "default";
        }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.ApplicationRole(string)'
        public ApplicationRole(string MyRole)
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ApplicationRole.ApplicationRole(string)'
        {
            this.MyRole = MyRole;
            this.Name = MyRole;
            this.Id = Guid.NewGuid();
        }
    }
}
