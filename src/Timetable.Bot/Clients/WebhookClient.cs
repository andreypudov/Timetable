﻿// <copyright file="WebhookClient.cs" company="Andrey Pudov">
//     Copyright (c) Andrey Pudov. All Rights Reserved. Licensed under the Apache License, Version 2.0. See LICENSE.txt in the project root for license information.
// </copyright>

namespace Timetable.Bot.Clients
{
    using System;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using HtmlAgilityPack;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using Telegram.Bot;
    using Telegram.Bot.Types;
    using Telegram.Bot.Types.Enums;
    using Telegram.Bot.Types.ReplyMarkups;
    using Timetable.Bot.Helpers;
    using Timetable.Bot.Types;
    using Authorization = Timetable.Bot.Helpers.Authorization;

    /// <summary>
    /// Represents the handler of web hook requests.
    /// </summary>
    public class WebhookClient
    {
        private readonly ITelegramBotClient client;
        private readonly Configuration configuration;
        private readonly ILogger logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebhookClient"/> class.
        /// </summary>
        /// <param name="configuration">The configuration of the bot.</param>
        /// <param name="logger">The instance of the logger.</param>
        public WebhookClient(Configuration configuration, ILogger logger)
        {
            this.configuration = configuration;
            this.logger = logger;
            this.client = new TelegramBotClient(configuration.Token);
        }

        /// <summary>
        /// Processes client request from the web hook.
        /// </summary>
        /// <param name="request">The HTTP request from the client.</param>
        /// <returns>The task that processes a request.</returns>
        public Task Update(string request)
        {
            this.logger.LogInformation($"{nameof(WebhookClient)} {request}");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var update = JsonConvert.DeserializeObject<Update>(request);
            return this.HandleUpdate(update);
        }

        private static string GetInnerTextValue(HtmlNode node)
        {
            return WebUtility.HtmlDecode(node.InnerText.Trim());
        }

        private async Task HandleUpdate(Update update)
        {
            this.logger.LogInformation($"{nameof(WebhookClient)} HandleUpdate");

            switch (update.Type)
            {
                case UpdateType.Message:
                    await this.OnMessageHandler(update).ConfigureAwait(false);
                    break;
                case UpdateType.CallbackQuery:
                    await this.OnCallbackQueryHandler(update).ConfigureAwait(false);
                    break;
            }
        }

        private async Task OnMessageHandler(Update update)
        {
            if (string.IsNullOrEmpty(update.Message.Text))
            {
                return;
            }

            if (Authorization.IsAuthorized(update.Message.Chat.FirstName + " " + update.Message.Chat.LastName) == false)
            {
                return;
            }

            this.Log(
                update.Message.Chat.Id,
                update.Message.Chat.Username,
                update.Message.Chat.FirstName,
                update.Message.Chat.LastName,
                update.Message.MessageId);

            switch (update.Message.Text.Trim())
            {
                case "/start":
                case "/timetable":
                    await this.OnTimetableAsync(update.Message.Chat.Id, TimetableType.General)
                        .ConfigureAwait(false);
                    break;
                case "/session":
                    await this.OnTimetableAsync(update.Message.Chat.Id, TimetableType.Session)
                        .ConfigureAwait(false);
                    break;
                default:
                    await this.client.SendTextMessageAsync(
                            chatId: update.Message.Chat,
                            text: $"Неверная команда: {update.Message.Text}\nИспользуйте /timetable для получения прогноза погоды.")
                        .ConfigureAwait(false);
                    break;
            }
        }

        private async Task OnCallbackQueryHandler(Update update)
        {
            if (string.IsNullOrEmpty(update.CallbackQuery.Data))
            {
                return;
            }

            if (Authorization.IsAuthorized(update.CallbackQuery.Message.Chat.FirstName + " " + update.CallbackQuery.Message.Chat.LastName) == false)
            {
                return;
            }

            this.Log(
                update.CallbackQuery.Message.Chat.Id,
                update.CallbackQuery.Message.Chat.Username,
                update.CallbackQuery.Message.Chat.FirstName,
                update.CallbackQuery.Message.Chat.LastName,
                update.CallbackQuery.Message.MessageId);

            try
            {
                await this.client.EditMessageReplyMarkupAsync(
                    update.CallbackQuery.Message.Chat.Id,
                    update.CallbackQuery.Message.MessageId).ConfigureAwait(false);
            }
            catch (AggregateException ex)
            {
                this.logger.LogError(ex.ToString());
            }

            switch (update.CallbackQuery.Data.Trim())
            {
                case "start":
                case "timetable":
                    await this.OnTimetableAsync(update.CallbackQuery.Message.Chat.Id, TimetableType.General)
                        .ConfigureAwait(false);
                    break;
                case "session":
                    await this.OnTimetableAsync(update.CallbackQuery.Message.Chat.Id, TimetableType.Session)
                        .ConfigureAwait(false);
                    break;
                default:
                    await this.client.SendTextMessageAsync(
                            chatId: update.CallbackQuery.Message.Chat,
                            text: $"Неверная команда: {update.CallbackQuery.Data}\nИспользуйте /timetable для получения прогноза погоды.")
                        .ConfigureAwait(false);
                    break;
            }
        }

        private async Task OnTimetableAsync(long chatId, TimetableType timetableType)
        {
            try
            {
                var timetable = await TimetableParser.GetAsync(this.configuration.Login, this.configuration.Password, timetableType);
                timetable = TimetableFormatter.Format(timetable, timetableType);
                await this.ReplyAsync(chatId, (timetable.Length > 0) ? timetable : "Расписание для студенческой группы отсутствует", timetableType);
            }
            catch (Exception e)
            {
                this.logger.LogError($"{nameof(WebhookClient)} OnTimetableAsync {e.Message}");
                await this.ReplyAsync(chatId, "Не удалось получить информацию о расписании для студенческой группы", timetableType);
            }
        }

        private async Task ReplyAsync(long chatId, string message, TimetableType timetableType)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    new InlineKeyboardButton()
                    {
                        Text = "Повторить запрос",
                        CallbackData = (timetableType == TimetableType.General) ? "timetable" : "session",
                    },
                },
            });

            await this.client.SendTextMessageAsync(
                chatId: chatId,
                text: message,
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                disableWebPagePreview: true).ConfigureAwait(false);
        }

        private void Log(long id, string userName, string firstName, string lastName, int messageId)
        {
            userName = WebUtility.HtmlDecode(userName);
            firstName = WebUtility.HtmlDecode(firstName);
            lastName = WebUtility.HtmlDecode(lastName);

            var message = $"[{id}] [{userName}] [{firstName} {lastName}] [{messageId}]";

            MissionMonitor.Publish($"{nameof(Timetable)} {message}");
            this.logger.LogInformation(message);
        }
    }
}
