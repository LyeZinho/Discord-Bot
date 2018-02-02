﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;

namespace WhalesFargo
{
    /**
     * MyGlobals
     * Static class to hold all global variables.
     */
    public static class MyGlobals
    {
        public static Boolean TrollUser = false;
        public static Boolean Debug = false; // Turn on for cmd printing
       
        public static Boolean PhraseRespond = false;

    }

   /**
    * Main program to run the discord bot.
    */
    class Program
    {
        // Private variables.
        private DiscordSocketClient m_Client; // Discord client.
        private CommandService m_Commands; // Command service to link modules.
        private IServiceProvider m_Services; // Service provider to add services to these modules.
        private string m_Token = ""; // Bot Token. Do not share with other people if you plan to hardcode it here. Otherwise create a BotToken.txt file.

        /**
         * Main
         */
        static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

       /**
        * MainAsync
        * 
        */
        public async Task MainAsync()
        {
            // Get the token from the application settings.
            m_Token = GetBotToken();

            // Start to make the connection to the server
            m_Client = new DiscordSocketClient();
            m_Commands = new CommandService();
            m_Services = InstallServices(); // We install services by adding it to a service collection.

            // Startup the client.
            await m_Client.LoginAsync(TokenType.Bot, m_Token); // Login using our defined token.
            await m_Client.StartAsync();

            // Install commands once the client has logged in.
            await InstallCommands();

            // Important for the publishing of GVG announcements!
            // Interval of 5 minutes
            const double interval5Minutes = 4 * 30 * 1000;
            // Creates a new system timer, and checks every 5 minutes for elapsed time.
            System.Timers.Timer checkForTime = new System.Timers.Timer(interval5Minutes);
            checkForTime.Elapsed += new ElapsedEventHandler(CheckForTime_ElapsedAsync);
            checkForTime.Enabled = true;

            // Doesn't end the program until the whole thing is done.
            await Task.Delay(-1);
        }

        /**
         * GetBotToken
         * Attempt to get the bot token from BotToken.txt
         * If it doesn't exist, we can't return anything.
         */
        private string GetBotToken()
        {
            string token = "";
            if (File.Exists("BotToken.txt"))
                token = File.ReadLines("BotToken.txt").First();
            return token;
        }

        /**
         * InstallServices
         * This is where you install all necessary services for our bot.
         * TODO: Make sure to add additional services here!!
         */
        private IServiceProvider InstallServices()
        {
            ServiceCollection services = new ServiceCollection();

            // Add all additional services here.
            services.AddSingleton<AudioService>(); // AudioModule : AudioService
            services.AddSingleton<ChatService>(); // ChatModule : ChatService

            // Return the service provider.
            return services.BuildServiceProvider();
        }

        /**
         * InstallCommands
         * This is where you install all possible commands for our bot.
         * Essentially, it will take the Messages Received and send it into our Handler 
         */
        private async Task InstallCommands()
        {
            // Before we install commands, we should check if everything was set up properly. Check if logged in.
            if (m_Client.LoginState != (LoginState.LoggedIn)) return;

            // Hook the MessageReceived Event into our Command Handler
            m_Client.MessageReceived += HandleCommand;

            // Add tasks to send Messages, and userJoined to appropriate places
            m_Client.Ready += DefaultBotStatus;
            m_Client.UserJoined += UserJoined;
            m_Client.UserLeft += UserLeft;
            m_Client.Log += Log;
            m_Client.Connected += Connected;
            m_Client.Disconnected += Disconnected;

            // Discover all of the commands in this assembly and load them.
            await m_Commands.AddModulesAsync(Assembly.GetEntryAssembly());
        }

        /**
         * HandleCommand
         * Handles commands with prefixes '!' and mention prefix.
         * Others get passed to TrolRogue and CheckMessageContentForResponse
         * @param messageParam   The command parsed as a SocketMessage.
         */
        private async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;
            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!(message.HasCharPrefix('!', ref argPos) || message.HasMentionPrefix(m_Client.CurrentUser, ref argPos)))
            {
                // If it isn't a command, check to see if rogue sent it.
                await TrollUser(messageParam);
                await CheckMessageContentForResponse(messageParam);
                return;
            }
            // Create a Command Context
            var context = new CommandContext(m_Client, message);
            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await m_Commands.ExecuteAsync(context, argPos, m_Services);
            if (!result.IsSuccess)
                await context.Channel.SendMessageAsync(result.ErrorReason);
        }

        /**
         * SetBotStatus
         * This sets the bots status as default. Can easily be changed. 
         */
        private async Task DefaultBotStatus()
        {
            await m_Client.SetGameAsync("Type !help for help!");
        }

        /**
         * UserJoined
         * This message is sent once a user joins the server.
         * 
         * @param user - A single user.
         */
        private async Task UserJoined(SocketGuildUser user)
        {
            var channel = user.Guild.DefaultChannel;  // You can add references to any channel you wish
            await channel.SendMessageAsync("Welcome to the Discord server" + user.Mention + "! Feel free to ask around if you need help!");
        }

        /**
         * UserLeft
         * This message is sent once a user joins the server. 
         * 
         * @param user - A single user.
         */
        private async Task UserLeft(SocketGuildUser user)
        {
            var channel = user.Guild.DefaultChannel; // You can add references to any channel you wish
            await channel.SendMessageAsync(user.Mention + " has left the Discord server.");
        }

        /**
         * Log
         * Bot will log to Console 
         * 
         * @param msg - Message to write out to Console.
         */
        private Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        /** 
        * Connected
        * Once fully connected, prints out here.
        */
        private Task Connected()
        {
            Console.WriteLine("Status : Connected");
            return Task.CompletedTask;
        }

        /** 
        * Disconnected
        * Handles if the bot is suddenly disconnected.
        * 
        * @param arg - Exception thrown if disconnected for any reason.
        */
        private Task Disconnected(Exception arg)
        {
            Console.WriteLine("Status : Disconnected \n" + arg);
            return Task.CompletedTask;
        }

        /** 
         * CheckMessageContentForResponse
         * Check contents of various messages sent and execute accordingly.
         * @param arg   Message parsed as a SocketUserMessage.
         */
        private async Task CheckMessageContentForResponse(SocketMessage arg)
        {
            var user = arg.Author;
            var chnl = arg.Channel as SocketTextChannel;
            var message = arg as SocketUserMessage;
            string str_message = message.ToString();
            string msg = WhaleHelp.GetResponseMessage(str_message);
            
            await chnl.SendMessageAsync(msg);
        }

        /** 
         * TrollUser
         * Check if the user id matches the troll id.
         * @param arg   Message parsed as a SocketUserMessage.
         */
        private async Task TrollUser(SocketMessage arg)
        {
            if (MyGlobals.TrollUser)
            {
                // User to troll's ID
                ulong userID = 339836073716744194;
                var user = arg.Author;
                var chnl = arg.Channel as SocketTextChannel;
                var message = arg as SocketUserMessage;
                if (user.Id == userID)
                {
                    Random rnd = new Random();
                    if ((rnd.Next(1, 100) % 2) == 0)
                    {
                        string msg = WhaleHelp.getTrollUserMessage();
                        await chnl.SendMessageAsync(msg);
                    }
                }
            }
        }

        /**
         * CheckForTime_ElapsedAsync
         * Checks time elapsed. Essential for the auto messasging of channels 
         * @param sender
         * @param s
         */
        public async void CheckForTime_ElapsedAsync(object sender, ElapsedEventArgs e)
        {
            string event_name = WhaleHelp.TimeIsReady();



            if (String.Equals(event_name, "colo", StringComparison.Ordinal)) await SendColo();
            else if (String.Equals(event_name, "gba", StringComparison.Ordinal)) await SendGba();
            else if (String.Equals(event_name, "gbc", StringComparison.Ordinal)) await SendGbc();
            else if (String.Equals(event_name, "mobius", StringComparison.Ordinal)) await SendMobius();
        }

        /** 
         * SendColo
         * Sends the Colo Notification 
         */
        public async Task SendColo()
        {
            // Gets the colo channel 
            var colochannel = m_Client.GetChannel(357943551264555010) as SocketTextChannel;
            /* You can add references to any channel you wish */
            await colochannel.SendMessageAsync("@everyone, Coliseum will begin shortly.");
        }

        public async Task SendMobius()
        {
            // Gets the colo channel 
            var colochannel = m_Client.GetChannel(408703204247863307) as SocketTextChannel;
            /* You can add references to any channel you wish */
            await colochannel.SendMessageAsync("@everyone, Mobius will begin shortly.");
        }

        /**
         * SendGb
         * Sends the Guild Battle Notification 
         */
        public async Task SendGba()
        {
            // Gets the colo channel 
            var colochannel = m_Client.GetChannel(223181247902515210) as SocketTextChannel;
            /* You can add references to any channel you wish */
            await colochannel.SendMessageAsync("<@&330052175973842946> <@&330052209440194581> , Guild Battle/Guild Raid will begin shortly.");
        }

        public async Task SendGbc()
        {
            // Gets the colo channel 
            var colochannel = m_Client.GetChannel(223181247902515210) as SocketTextChannel;
            /* You can add references to any channel you wish */
            await colochannel.SendMessageAsync("<@&405854488478613515> , Guild Battle/Guild Raid will begin shortly.");
        }


    }
}
