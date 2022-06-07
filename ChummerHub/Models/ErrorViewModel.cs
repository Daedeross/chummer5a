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
namespace ChummerHub.Models
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel'
    public class ErrorViewModel
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel'
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel.RequestId'
        public string RequestId { get; set; }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel.RequestId'

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel.ShowRequestId'
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member 'ErrorViewModel.ShowRequestId'
    }
}
