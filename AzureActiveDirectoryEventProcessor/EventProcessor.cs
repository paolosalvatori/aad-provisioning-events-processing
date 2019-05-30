#region Copyright
//=======================================================================================
// Microsoft Azure Customer Advisory Team 
//
// This sample is supplemental to the technical guidance published on my personal GitHub
// account at http://github.com/paolosalvatori 
// 
// Author: Paolo Salvatori
//=======================================================================================
// Copyright (c) Microsoft Corporation. All rights reserved.
// 
// LICENSED UNDER THE APACHE LICENSE, VERSION 2.0 (THE "LICENSE"); YOU MAY NOT USE THESE 
// FILES EXCEPT IN COMPLIANCE WITH THE LICENSE. YOU MAY OBTAIN A COPY OF THE LICENSE AT 
// http://www.apache.org/licenses/LICENSE-2.0
// UNLESS REQUIRED BY APPLICABLE LAW OR AGREED TO IN WRITING, SOFTWARE DISTRIBUTED UNDER THE 
// LICENSE IS DISTRIBUTED ON AN "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY 
// KIND, EITHER EXPRESS OR IMPLIED. SEE THE LICENSE FOR THE SPECIFIC LANGUAGE GOVERNING 
// PERMISSIONS AND LIMITATIONS UNDER THE LICENSE.
//=======================================================================================
#endregion

#region Using Directives
using System;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventHubs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SendGrid;
using SendGrid.Helpers.Mail;
using Microsoft.Extensions.Logging;
#endregion

namespace Microsoft.AzureCat.Functions
{
	public static class EventProcessor
    {
        #region Private Constants
        private const string BodyCssStyle = "background:#E9F4DC; font:14px 'Calibri'; padding:20px;";
        private const string PCssStyle = "font:14px 'Calibri'";
        private const string TableCssStyle = "width: 100%";
        private const string THeadTrCssStyle = "height:32px; background:#FFED86; padding-left:8px; font:14px 'Calibri';";
        private const string TBodyTrCssStyle = "height:32px; background:#FFFFFF; padding-left:8px; font:14px 'Calibri';";
        private const string FirstThCssStyle = "width: 300px; text-align:left;";
        private const string SecondThCssStyle = "text-align:left;";

        #endregion

        #region Private Static Fields
        private static readonly bool debug = bool.Parse(Environment.GetEnvironmentVariable("Debug"));
        private static readonly string fromEmailAddress = Environment.GetEnvironmentVariable("FromEmailAddress");
		private static readonly string toEmailAddress = Environment.GetEnvironmentVariable("ToEmailAddress"); 
		private static readonly string azureActiveDirectoryName = Environment.GetEnvironmentVariable("AzureActiveDirectoryName");
        #endregion

        #region Function Method
        [FunctionName("EventProcessor")]
		public static void Run([EventHubTrigger("%EventHub%", Connection = "EventHubConnectionString", ConsumerGroup = "%ConsumerGroup%")]EventData[] eventDataArray,
							   [SendGrid(ApiKey = "SendGridApiKey")] out SendGridMessage message,
                               ILogger log)
		{
			try
			{
				if (!ValidateSettings(log))
				{
					message = null;
					return;
				}

				var eventInfoList = new List<EventInfo>();
                
                foreach (var eventData in eventDataArray)
                {
                    var json = Encoding.UTF8.GetString(eventData.Body.Array);
                    var jObject = JObject.Parse(json);
                    var jTokens = jObject.SelectTokens("$.records[*]");
                    foreach (var jToken in jTokens)
                    {
                        try
                        {
                            var eventInfo = ProcessEvent(jToken, log);
                            if (eventInfo != null)
                            {
                                eventInfoList.Add(eventInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.LogError("An error occurred:", ex, "ServiceFabricEventProcessor");
                        }
                    }
                }
                message = CreateEmailMessage(eventInfoList, log);
			}
			catch (Exception ex)
			{
				log.LogError("An error occurred:", ex, "ServiceFabricEventProcessor");
				throw;
			}
		}
		#endregion

		#region Private Methods
		private static EventInfo ProcessEvent(JToken jToken, ILogger log)
		{
            var operationCategoryToken = jToken.SelectToken("$.properties.category");
            if (operationCategoryToken == null)
            {
                return null;
            }
            var operationCategory = operationCategoryToken.Value<string>();

            if (string.Compare(operationCategory, "UserManagement", true) != 0 &&
                string.Compare(operationCategory, "GroupManagement", true) != 0)
            {
                return null;
            }

            var operationNameToken = jToken.SelectToken("$.operationName");
            if (operationNameToken == null)
            {
                return null;
            }
            var operationName = operationNameToken.Value<string>();

            var eventCategoryToken = jToken.SelectToken("$.category");
            if (eventCategoryToken == null)
            {
                return null;
            }
            var eventCategory = eventCategoryToken.Value<string>();

            var tenantIdToken = jToken.SelectToken("$.tenantId");
            if (tenantIdToken == null)
            {
                return null;
            }
            var tenantId = tenantIdToken.Value<string>();
            
            var initiatedByToken = jToken.SelectToken("$.properties.initiatedBy.user.userPrincipalName");
            if (initiatedByToken == null)
            {
                return null;
            }
            var initiatedBy = initiatedByToken.Value<string>();

            var userPrincipalNameToken = jToken.SelectToken("$.properties.targetResources[0].userPrincipalName");
            if (userPrincipalNameToken == null)
            {
                return null;
            }
            var userPrincipalName = userPrincipalNameToken.Value<string>();

            string groupDisplayName = null;
            
            if (string.Compare(operationCategory, "GroupManagement", true) == 0)
            {
                if (string.Compare(operationName, "Add member to group") == 0)
                {
                    var groupDisplayNameTokens = jToken.SelectTokens("$.properties.targetResources[0].modifiedProperties[?(@.displayName == 'Group.DisplayName')].newValue");
                    if (groupDisplayNameTokens != null && !groupDisplayNameTokens.Any())
                    {
                        return null;
                    }
                    groupDisplayName = groupDisplayNameTokens.ToList()[0].Value<string>();
                    if (!string.IsNullOrWhiteSpace(groupDisplayName))
                    {
                        groupDisplayName = groupDisplayName.Replace("\"", "");
                    }
                }
                else
                {
                    var groupDisplayNameToken = jToken.SelectToken("$.properties.targetResources[0].displayName");
                    if (groupDisplayNameToken == null)
                    {
                        return null;
                    }
                    groupDisplayName = groupDisplayNameToken.Value<string>();
                }
            }

            var eventIdToken = jToken.SelectToken("$.properties.id");
			if (eventIdToken == null)
			{
				return null;
			}
            var eventId = eventIdToken.Value<string>();

            var eventTimeToken = jToken.SelectToken("$.time");
			if (eventTimeToken == null)
			{
				return null;
			}
            var eventTime = eventTimeToken.Value<string>();

            var eventInfo = new EventInfo
			{
				EventId = eventId,
                EventTime = eventTime,
                EventCategory = eventCategory,
                OperationName = operationName,
                OperationCategory = operationCategory,
                TenantId = tenantId,
                InitiatedBy = initiatedBy,
                UserPrincipalName = userPrincipalName,
                GroupName = groupDisplayName
            };
			
            if (debug)
            {
                var json = jToken.ToString();
                log.LogInformation(json);
                Debug.WriteLine(json);
            }

            return eventInfo;
        }

		private static SendGridMessage CreateEmailMessage(List<EventInfo> events, ILogger log)
		{
			if (events == null || events.Count == 0)
			{
				return null;
			}
			var message = new SendGridMessage();
			message.AddTo(toEmailAddress);
            message.AddContent(MimeType.Html, CreateEmailBody(events, log));
			message.SetFrom(new EmailAddress(fromEmailAddress, $"{azureActiveDirectoryName} Azure Active Directory Alert Service"));
			message.SetSubject($"Alert Notification for Azure Active Directory: {azureActiveDirectoryName}");
            message.AddHeader("X-Priority", "1");
            message.AddHeader("X-MSMail-Priority", "High");
            return message;
		}

		private static string CreateEmailBody(List<EventInfo> events, ILogger log)
		{
			var builder = new StringBuilder($"<html><head></head><body style=\"{BodyCssStyle}\">");
			builder.AppendLine($"<p style=\"{PCssStyle}\">The following events occurred in the <b>{azureActiveDirectoryName}</b> Azure Active Directory:</p>");
           
			foreach (var eventInfo in events)
			{
				try
				{
					builder.AppendLine($"<table style=\"{TableCssStyle}\"><thead><tr style=\"{THeadTrCssStyle}\"><th style=\"{FirstThCssStyle}\">Property</th><th style=\"{SecondThCssStyle}\">Value</th></tr><thead><tbody>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>EventId</td><td>{eventInfo.EventId}</td></tr>");
					builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>EventTime</td><td>{eventInfo.EventTime}</td></tr>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>EventCategory</td><td>{eventInfo.EventCategory}</td></tr>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>OperationName</td><td>{eventInfo.OperationName}</td></tr>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>OperationCategory</td><td>{eventInfo.OperationCategory}</td></tr>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>TenantId</td><td>{eventInfo.TenantId}</td></tr>");
                    builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>InitiatedBy</td><td>{eventInfo.InitiatedBy}</td></tr>");
                    if (!string.IsNullOrWhiteSpace(eventInfo.UserPrincipalName))
                    {
                        builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>UserPrincipalName</td><td>{eventInfo.UserPrincipalName}</td></tr>");
                    }
                    if (!string.IsNullOrWhiteSpace(eventInfo.GroupName))
                    {
                        builder.AppendLine($"<tr style=\"{TBodyTrCssStyle}\"><td>GroupName</td><td>{eventInfo.GroupName}</td></tr>");
                    }
					builder.Append("</tbody></table><br><p>");
				}
				catch (Exception ex)
				{
					log.LogError("An error occurred:", ex, "ServiceFabricEventProcessor");
				}
			}
			builder.AppendLine("</body></html>");
			return builder.ToString();
		}
		
		private static bool ValidateSettings(ILogger log)
		{
			var warnings = new List<string>();
			if (string.IsNullOrEmpty(fromEmailAddress))
			{
				warnings.Add(nameof(fromEmailAddress));
			}
			if (string.IsNullOrEmpty(toEmailAddress))
			{
				warnings.Add(nameof(toEmailAddress));
			}
			if (string.IsNullOrEmpty(azureActiveDirectoryName))
			{
				warnings.Add(nameof(azureActiveDirectoryName));
			}
			var ok = !warnings.Any();
			if (!ok)
			{
				log.LogWarning($"The following settings are null or empty: {string.Join(", ", warnings)}. The execution cannot proceed.");
			}
			return ok;
		}
		#endregion
	}
}
