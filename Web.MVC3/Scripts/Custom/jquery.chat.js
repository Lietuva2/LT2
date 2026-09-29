(function ($) {
    $.fn.chat = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        }
        else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.chat.defaults = {
        userProfileUrl: "/Account/Details/",
        myName: "aš",
        debug: true
    };

    var methods = {
        init: function (options) {
            opts = $.extend({}, $.fn.chat.defaults, options);
            return this.each(function () {
                var $this = $(this);
                $this.on('click', '[data-action="chat"]', function (e) {
                    chatWith($(this).data('chatid'), $(this).data('chatname'), $(this).is('.connected'));
                    return $.helpers.cancelEvent(e);
                });

                $this.on('click', '[data-action="groupchat"]', function (e) {
                    groupChat($(this).data('chatid'), $(this).data('chatname'), $(this).data('url'));
                    return $.helpers.cancelEvent(e);
                });
            });
        },
        enable: function (groupId) {
            chatGroupId = groupId;
            $('#chat').show();
        },
        chat: function (chatId, chatname, isOnline) {
            chatWith(chatId, chatname, isOnline);
        },
        groupChat: function (chatId, chatname) {
            groupChat(chatId, chatname);
        },
        stopAllClients: function () {
            chatHub.server.stopAllClients();
            $.connection.hub.stop();
        }
    };

    var originalTitle = document.title;
    var chatGroupId;
    var dialogBlink = new Object();
    var opts = $.fn.chat.defaults;
    var typingTimeouts = new Array();

    var chatHub = $.connection.chatHub;

    function getFormattedMessage(msg, excludeSender) {
        var from = excludeSender ? '' : '<span class="chatboxmessagefrom">' + msg.FullName + '</span>: ';
        return '<div class="chatboxmessage" title="' + msg.DateString + '">' + from + '<span class="chatboxmessagecontent">' + msg.Message + '</span></div>';
    }

    function ensureOpen(box, chatId, chatName, isGroup, isOnline, url) {
        var doRestore = false;
        if (box.length <= 0) {
            //alert(item.u);
            createChatBox(chatId, chatName, isGroup, isOnline, url);
            box = $("#chatbox_" + chatId);
            doRestore = true;
        }
        if (box.is(':hidden')) {
            box.show();
            restructureChatBoxes();
            doRestore = true;
        }

        if (doRestore) {
            restoreChatBoxState(chatId);
        }

        return box;
    }

    function excludeSender(content, senderName) {
        return content.find('.chatboxmessagefrom:last').text() == senderName;
    }

    // Add client-side hub methods that the server will call
    $.extend(chatHub.client, {
        newMessage: function (message, chatId, chatName, isGroup, url, callerId) {
            var box = $("#chatbox_" + chatId);

            //if message is sent to caller's other clients, chatName is not set
            if (chatName) {
                box = ensureOpen(box, chatId, chatName, isGroup, true, url);
            }

            if (box.length > 0) {
                var content = box.find(".chatboxcontent");
                content.append(getFormattedMessage(message, excludeSender(content, message.FullName)));
                if (content.length > 0) {
                    content.scrollTop(content[0].scrollHeight);
                }

                if (callerId != opts.currentUserId && chatName) {
                    if (box.is(':focus') || box.find('.chatboxtextarea').is(':focus')) {
                        chatHub.server.stopBlinking();
                    } else {
                        var alert;
                        if (isGroup) {
                            alert = "Diskusija: " + originalTitle;
                        } else {
                            alert = chatName + ' sako..';
                        }

                        $.titleAlert(alert, {
                            requireBlur: true,
                            interval: 1000,
                            stopOnFocusCallback: function () {
                                chatHub.server.stopBlinking();
                            }
                        });

                        clearInterval(dialogBlink[chatId]);
                        dialogBlink[chatId] = setInterval(function () {
                            box.find('.chatboxhead').toggleClass('chatboxblink');
                        }, 1000);
                    }
                }
            }
        },
        messageHistory: function (messageList, chatId, chatName, isOnline, isGroup, url) {
            var box = $("#chatbox_" + chatId);
            box = ensureOpen($("#chatbox_" + chatId), chatId, chatName, isGroup, isOnline, url);

            var content = box.find(".chatboxcontent").empty();
            for (var i = messageList.length - 1; i >= 0; i--) {
                var msg = messageList[i];
                content.append(getFormattedMessage(msg, excludeSender(content, msg.FullName)));
            }
            //for (var msgKey in messageList) {
            //    var msg = messageList[msgKey];
            //    content.append(getFormattedMessage(msg, excludeSender(content, msg.FullName)));
            //}
            if (content.length > 0) {
                content.scrollTop(content[0].scrollHeight);
            }
        },
        addUser: function (id, name, isOnline) {
            if ($('#activeUsers li[data-chatid="' + id + '"]').length == 0) {
                var className = isOnline === true ? "connected" : "disconnected";
                $('#activeUsers').append("<li class='" + className + "' data-action='chat' data-chatid='" + id + "' data-chatname='" + name + "'><span class='presenceIcon'></span><a href='" + opts.userProfileUrl + id + "'>" + name + "</a></li>");
            }
        },
        removeUser: function (id) {
            $('#activeUsers li[data-chatid="' + id + '"]').remove();
        },
        userConnected: function (id) {
            var box = $("#chatbox_" + id);
            box.addClass('connected');
            box.removeClass('disconnected');

            var item = $('#activeUsers li[data-chatid="' + id + '"]');
            item.addClass('connected');
            item.removeClass('disconnected');
        },
        userDisconnected: function (id) {
            var box = $("#chatbox_" + id);
            box.addClass('disconnected');
            box.removeClass('connected');

            var item = $('#activeUsers li[data-chatid="' + id + '"]');
            item.addClass('disconnected');
            item.removeClass('connected');
        },
        userIsTyping: function (id) {
            var box = $("#chatbox_" + id);
            box.addClass('istyping');
        },
        userNotTyping: function (id) {
            var box = $("#chatbox_" + id);
            box.removeClass('istyping');
        },
        stopBlinking: function () {
            stopDialogBlink();
            $.titleAlert("stop");
        },
        groupMessageCount: function (cnt) {
            if (cnt && cnt > 0) {
                var btn = $('#groupChat').find('button');
                if (btn.text().indexOf('(') == -1) {
                    btn.text(btn.text() + " (" + cnt + ")");
                }
            }
        },
        stopClient: function() {
            $.connection.hub.stop();
        }
    });

    $.connection.hub.reconnected(function () {
        chatHub.server.login(chatGroupId);
    });

    $.connection.hub.disconnected(function () {
        setTimeout(function () {
            startConnection();
        }, 5000); // Restart connection after 5 seconds.
    });

    function startConnection() {
        $.connection.hub.logging = opts.debug;

        $.connection.hub.start().done(function () {
            chatHub.server.login(chatGroupId);
        }).fail(function () { console.log("Could not connect to chat server!"); });
    }

    startConnection();

    function chatWith(chatId, chatname, isOnline) {
        chat(chatId, chatname, false, isOnline);
    }

    function groupChat(chatId, chatname, url) {
        if (!url) {
            url = window.location.href;
        }

        chat(chatId, chatname, true, null, url);
        chatHub.server.openGroupDialog(chatId, chatname, url);
    }

    function chat(chatId, chatname, isGroupChat, isOnline, url) {
        createChatBox(chatId, chatname, isGroupChat, isOnline, url);
        $("#chatbox_" + chatId + " .chatboxtextarea").focus();
    }

    function restructureChatBoxes() {
        var width = 20;
        $('.chatbox').each(function () {
            var box = $(this);
            box.css('right', width + 'px');

            if (!box.is(':hidden')) {

                width += (225 + 7) + 20;
            }
        });
    }

    function createChatBox(chatboxtitle, chatname, isGroupChat, isOnline, url) {
        if (!isGroupChat) {
            chatHub.server.openDialog(chatboxtitle);
        }

        var box = $("#chatbox_" + chatboxtitle);
        if (box.length > 0) {

            if (box.is(':hidden')) {
                box.show();
                restructureChatBoxes();
            }
            box.find(".chatboxtextarea").focus();
            return;
        }
        if (!url) {
            url = opts.userProfileUrl + chatboxtitle;
        }

        var chatTitle = '<a href="' + url + '">' + chatname + '</a>';
        var typing = !isGroupChat ? '<div class="typing"><span>' + chatname + ' rašo..<span></div>' : '';
        var unsubscribe = isGroupChat ? '<a href="javascript:void(0)" class="chatboxUnsubscribe" title="Palikti pokalbį"> ▼ </a>' : '';
        var presence = isGroupChat ? '' : '<span class="presenceIcon"/>';
        box = $(" <div />").attr("id", "chatbox_" + chatboxtitle)
            .addClass("chatbox")
            .html('<div class="chatboxHeader">' +
                '<div class="chatboxhead">' + presence + '<div class="chatboxtitle" title="' + chatname + '">' + chatTitle + '</div>' +
                '<div class="chatboxoptions">' +
                '<a href="javascript:void(0)" class="chatboxHeader" title="Sumažinti/padidinti"> _ </a>' +
                unsubscribe +
                '<a href="javascript:void(0)" class="chatboxClose" title="Uždaryti langelį"> X </a></div><br clear="all"/></div></div>' +
                '<div class="contentContainer"><div class="historyButtons">' +
                '<a data-history="Today" class="historyButton" href="javascript:void(0);">Šiandien</a><span class="dot">.</span>' +
                '<a data-history="LastWeek" class="historyButton" href="javascript:void(0);">Savaitė</a><span class="dot">.</span>' +
                '<a data-history="LastMonth" class="historyButton" href="javascript:void(0);">Mėnuo</a><span class="dot">.</span>' +
                '<a data-history="All" class="historyButton" href="javascript:void(0);">Viskas</a></div>' +
                '<div class="chatboxcontent"></div>' + typing +
                '<div class="chatboxinput"><textarea class="chatboxtextarea"></textarea></div></div>');

        if (isOnline === true) {
            box.addClass("connected");
        } else if (isOnline === false) {
            box.addClass("disconnected");
        }

        box.find('.chatboxHeader').on('click', function (e) {
            //do not minimize on username click
            if ($(e.target).is('a') === false || $(e.target).is('.chatboxHeader')) {
                toggleChatBoxGrowth(chatboxtitle);
                return $.helpers.cancelEvent(e);
            }
        });

        if (isGroupChat) {
            box.find('a[data-history]').on('click', function (e) {
                chatHub.server.getGroupHistory(chatboxtitle, $(this).data('history'));
                return $.helpers.cancelEvent(e);
            });
        } else {
            box.find('a[data-history]').on('click', function (e) {
                chatHub.server.getHistory(chatboxtitle, $(this).data('history'));
                return $.helpers.cancelEvent(e);
            });
        }

        box.find('.chatboxClose').on('click', function (e) {
            closeChatBox(chatboxtitle);
            return $.helpers.cancelEvent(e);
        });

        box.find('.chatboxUnsubscribe').on('click', function (e) {
            if (confirm('Ar tikrai norite palikti pokalbį?')) {
                unsubscribeGroup(chatboxtitle);
                return $.helpers.cancelEvent(e);
            }
        });

        box.find('.chatboxtextarea').on('keydown', function (e) {
            return checkChatBoxInputKey(e, this, chatboxtitle, chatname, isGroupChat);
        });

        box.appendTo($("body"));
        if (isGroupChat) {
            var content = box.find('.chatboxcontent');
            content.height(content.height() + 18);
        }

        box.find(".chatboxtextarea").blur(function () {
            box.find(".chatboxtextarea").removeClass('chatboxtextareaselected');
        }).focus(function () {
            stopDialogBlink(chatboxtitle);
            $(this).addClass('chatboxtextareaselected');
        });

        box.click(function () {
            if ($(this).find('.chatboxcontent').css('display') != 'none') {
                $(this).find(".chatboxtextarea").focus();
            }
        });

        restructureChatBoxes();

        box.show();
    }

    function stopDialogBlink(boxId) {
        var box;
        if (!boxId) {
            box = $('.chatbox');
        } else {
            box = $('#chatbox_' + boxId);
        }

        box.find('.chatboxhead').removeClass('chatboxblink');
        if (dialogBlink) {
            if (boxId) {
                clearInterval(dialogBlink[boxId]);
                dialogBlink[boxId] = null;
            } else {
                for (var prop in dialogBlink) {
                    if (dialogBlink.hasOwnProperty(prop)) {
                        clearInterval(dialogBlink[prop]);
                        dialogBlink[prop] = null;
                    }
                }
            }
        }
    }

    function closeChatBox(chatboxtitle) {
        $('#chatbox_' + chatboxtitle).hide();
        restructureChatBoxes();

        chatHub.server.closeDialog(chatboxtitle);
    }

    function unsubscribeGroup(groupId) {
        $('#chatbox_' + groupId).hide();
        restructureChatBoxes();

        chatHub.server.unsubscribeGroup(groupId);
    }

    function toggleChatBoxGrowth(chatboxtitle) {
        var box = $('#chatbox_' + chatboxtitle);
        var chatboxContent = box.find('.chatboxcontent');
        if (chatboxContent.is(':hidden')) {

            persistChatBoxState(chatboxtitle, false);
            var content = box.find('.contentContainer').show();
            if (chatboxContent.length > 0) {
                chatboxContent.scrollTop(chatboxContent[0].scrollHeight);
            }
        } else {

            persistChatBoxState(chatboxtitle, true);
            box.find('.contentContainer').hide();
        }
    }

    function persistChatBoxState(boxId, minimized) {
        var newCookie = '';
        if (!minimized) {
            var minimizedChatBoxes = new Array();

            if ($.cookie('chatbox_minimized')) {
                minimizedChatBoxes = $.cookie('chatbox_minimized').split(/\|/);
            }

            newCookie = '';

            for (i = 0; i < minimizedChatBoxes.length; i++) {
                if (minimizedChatBoxes[i] != boxId) {
                    newCookie += minimizedChatBoxes[i] + '|';
                }
            }

            newCookie = newCookie.slice(0, -1);


            $.cookie('chatbox_minimized', newCookie, { path: '/' });
        } else {
            newCookie = boxId;

            if ($.cookie('chatbox_minimized')) {
                newCookie += '|' + $.cookie('chatbox_minimized');
            }

            $.cookie('chatbox_minimized', newCookie, { path: '/' });
        }
    }

    function restoreChatBoxState(boxId) {
        var box = $('#chatbox_' + boxId);
        var minimizedChatBoxes = new Array();

        if ($.cookie('chatbox_minimized')) {
            minimizedChatBoxes = $.cookie('chatbox_minimized').split(/\|/);
        }

        minimizeOrRestoreChatBox(box, $.inArray(boxId, minimizedChatBoxes) == -1);
    }

    function minimizeOrRestoreChatBox(box, show) {
        box.find('.contentContainer').toggle(show);
    }

    function checkChatBoxInputKey(event, chatboxtextarea, chatboxtitle, chatname, isGroupChat) {
        var box = $("#chatbox_" + chatboxtitle);
        if (event.keyCode == 13 && event.shiftKey == 0) {

            var message = $(chatboxtextarea).val();
            message = message.replace(/^\s+|\s+$/g, "");
            $(chatboxtextarea).val('');
            $(chatboxtextarea).focus();
            $(chatboxtextarea).css('height', '44px');
            if (message != '') {
                var content = box.find(".chatboxcontent");
                var msg = { DateString: new Date() + '', FullName: opts.myName, Message: message };
                content.append(getFormattedMessage(msg, excludeSender(content, msg.FullName)));
                content.scrollTop(content[0].scrollHeight);

                if (isGroupChat) {
                    chatHub.server.sendGroupMessage(message, chatboxtitle, chatname, location.href);
                } else {
                    chatHub.server.sendMessage(message, chatboxtitle);
                }
            }

            return false;
        }

        if (!isGroupChat) {
            if (typingTimeouts[chatboxtitle]) {
                clearTimeout(typingTimeouts[chatboxtitle]);
            } else {
                chatHub.server.userIsTyping(chatboxtitle);
            }

            typingTimeouts[chatboxtitle] = setTimeout(function () {
                chatHub.server.userNotTyping(chatboxtitle);
                typingTimeouts[chatboxtitle] = null;
            }, 2000);
        }

        var adjustedHeight = chatboxtextarea.clientHeight;
        var maxHeight = 94;

        if (maxHeight > adjustedHeight) {
            adjustedHeight = Math.max(chatboxtextarea.scrollHeight, adjustedHeight);
            adjustedHeight = Math.min(maxHeight, adjustedHeight);

            if (adjustedHeight > chatboxtextarea.clientHeight)
                $(chatboxtextarea).css('height', adjustedHeight + 8 + 'px');
        } else {
            $(chatboxtextarea).css('overflow', 'auto');
        }
    }
})(jQuery);