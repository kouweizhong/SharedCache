﻿#region Copyright (c) Roni Schuetz - All Rights Reserved
// * --------------------------------------------------------------------- *
// *                              Roni Schuetz                             *
// *              Copyright (c) 2010 All Rights reserved                   *
// *                                                                       *
// * Shared Cache high-performance, distributed caching and    *
// * replicated caching system, generic in nature, but intended to         *
// * speeding up dynamic web and / or win applications by alleviating      *
// * database load.                                                        *
// *                                                                       *
// * This Software is written by Roni Schuetz (schuetz AT gmail DOT com)   *
// *                                                                       *
// * This library is free software; you can redistribute it and/or         *
// * modify it under the terms of the GNU Lesser General Public License    *
// * as published by the Free Software Foundation; either version 2.1      *
// * of the License, or (at your option) any later version.                *
// *                                                                       *
// * This library is distributed in the hope that it will be useful,       *
// * but WITHOUT ANY WARRANTY; without even the implied warranty of        *
// * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU      *
// * Lesser General Public License for more details.                       *
// *                                                                       *
// * You should have received a copy of the GNU Lesser General Public      *
// * License along with this library; if not, write to the Free            *
// * Software Foundation, Inc., 59 Temple Place, Suite 330,                *
// * Boston, MA 02111-1307 USA                                             *
// *                                                                       *
// *       THIS COPYRIGHT NOTICE MAY NOT BE REMOVED FROM THIS FILE.        *
// * --------------------------------------------------------------------- *
#endregion

// *************************************************************************
//
// Name:      HttpHandlersConfigHandler.cs
// 
// Modified:  28-01-2010 SharedCache.com, chrisme  : clean up code
// ************************************************************************* 

using System.Configuration;
using System.Xml;

namespace SharedCache.WinServiceCommon.HttpHandlers
{
	/// <summary>
	/// Summary description for CustomConfigHandler.
	/// </summary>
	public class HttpHandlersConfigHandler : IConfigurationSectionHandler
	{
		/// <summary></summary>
		public HttpHandlersConfigHandler()
		{
		}

		#region IConfigurationSectionHandler Members

		/// <summary>
		/// 
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="configContext"></param>
		/// <param name="section"></param>
		/// <returns></returns>
		public object Create(object parent, object configContext, XmlNode section)
		{

			var config = new HttpHandlersConfig((HttpHandlersConfig)parent);
			config.LoadValuesFromConfigurationXml(section);
			return config;

		}

		#endregion
	}
}
