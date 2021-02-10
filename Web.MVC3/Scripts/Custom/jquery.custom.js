(function ($) {
    $.extend({
        getGo: function (url, params) {
            document.location = url + '?' + $.param(params);
        },
        postGo: function (url, params) {
            var $form = $("<form>")
                .attr("method", "post")
                .attr("action", url);
            $.each(params, function (name, value) {
                if (name != null && value != null) {
                    $("<input type='hidden'>")
                        .attr("name", name)
                        .attr("value", value)
                        .appendTo($form);
                }
            });
            $form.appendTo("body");
            $form.submit();
        }
    });
})(jQuery);

(function ($) {
    $.helpers = {};

    $.helpers.settings = {
        dialogSelector: "#dialog",
        dialogContentSelector: "#divDialogContent",
        loginDialogHeight: 590,
        loginDialogWidth: 420,
        loaderHtml: '<span class="loader"><img alt="" src="{0}" /></span>',
        relativeRoot: null,
        embedlyKey: null,
        setFacebookPermissionGrantedUrl: null,
        setPostedToFacebookUrl: null
    };

    $.helpers.checkDefault = function (arg, def) {
        return (typeof arg == 'undefined' ? def : arg);
    };

    $.helpers.identifyUser = function () {
        $('#dialog-confirm-question').dialog({
            resizable: false,
            modal: true,
            autoOpen: true,
            height: 'auto',
            width: '400px',
            closeOnEscape: true,
            buttons: [{
                text: JavaScriptLibraryResources.Agree,
                click: function () {
                    $(this).dialog("close");
                    $("#dialog-confirm-user").dialog({
                        resizable: false,
                        modal: true,
                        autoOpen: true,
                        height: 'auto',
                        width: 'auto',
                        closeOnEscape: true
                    });
                }
            }]
        });
    };
    
    $.helpers.identifyUserViisp = function () {
        $('#dialog-confirm-question').dialog({
            resizable: false,
            modal: true,
            autoOpen: true,
            height: 'auto',
            width: '400px',
            closeOnEscape: true,
            buttons: [{
                text: JavaScriptLibraryResources.Agree,
                click: function () {
                    $(this).dialog("close");
                    $("#dialog-confirm-user-viisp").dialog({
                        resizable: false,
                        modal: true,
                        autoOpen: true,
                        height: 'auto',
                        width: '700px',
                        closeOnEscape: true
                    });
                }
            }]
        });
        //$("#dialog-confirm-user-viisp").dialog({
        //    resizable: false,
        //    modal: true,
        //    autoOpen: true,
        //    height: 'auto',
        //    width: 'auto',
        //    closeOnEscape: true
        //});
    };
    $.helpers.showLoader = function (el, hideLink) {
        var loader = $($.helpers.settings.loaderHtml);
        loader.appendTo(el).show();
        if (hideLink) {
            hideLink.hide();
        }
    };

    $.helpers.hideLoader = function () {
        $('.loader').remove();
    };

    $.helpers.htmlEncode = function (value) {
        return $('<div/>').text(value).html();
    };

    $.helpers.htmlDecode = function (value) {
        return $('<div/>').html(value).text();
    };

    $.helpers.bindPreview = function (el) {
        el.on('click', '.thumbnail.video a, .thumbnail.rich a, .item .attributes a:not([href])', function (e) {
            var preview = $.helpers.htmlDecode($(this).parents('.item').data('preview'));
            if (!preview) {
                preview = $(this).parents('.item').data('preview');
            }

            var $preview = $(preview);
            var src = $preview.attr('src');
            
            var w = $(this).parents('.item').width();
            var h = w * $preview.attr('height') / $preview.attr('width');
            if (isNaN(Number(h))) {
                w = w * 9 / 16;
            }

            $preview = $preview.attr('width', w).attr('height', h);

            $(this).parents('.item').replaceWith($preview);
            return $.helpers.cancelEvent(e);
        });
    };

    $.helpers.setRichEditorText = function(id, text) {
        if (typeof(CKEDITOR) != 'undefined') {
            if (CKEDITOR && CKEDITOR.instances && CKEDITOR.instances[id]) {
                CKEDITOR.instances[id].setData(text);
            }
        }
    };

    $.helpers.loadAsync = function (e, url, data, callback, showLoader, hideLink, errorCallback) {
        if (typeof (showLoader) == "undefined") {
            showLoader = true;
        }

        var antiforgedData = typeof (data) == 'function' ? data() : data;
        if (!data) {
            antiforgedData = new Object();
        }
        antiforgedData["__RequestVerificationToken"] = $('#__AjaxAntiForgeryForm [name=__RequestVerificationToken]').val();

        return $.ajax({
            url: url,
            type: "POST",
            processData: true,
            data: antiforgedData,
            dataType: "json",
            cache: false,
            beforeSend: function () {
                if (showLoader) {
                    if (e) {
                        $.helpers.showLoader($(e.target).parent(), hideLink ? $(e.target) : null);
                    }
                }
            },
            complete: function () {
                if (showLoader) {
                    $.helpers.hideLoader();
                }
            },
            success: function (result) {
                if ($.helpers.handleUnauthorizedError(result)) {
                    return false;
                }
                if (hideLink && e) {
                    $(e.target).show();
                }

                if (callback) {
                    callback(result);
                }

                if (result.Content && typeof $.fn.autosize == 'function') {
                    $('textarea').autosize();
                }
            },
            error: function (response) {
                if (!$.helpers.handleUnauthorizedError(response)) {
                    var resp = $.parseJSON(response.responseText);
                    if (resp) {
                        if (resp.Message.indexOf('anti-forgery token was') >= 0) {
                            //alert('Jūsų prisijungimo laikas baigėsi. Paspaudus OK, puslapis bus perkrautas');
                            window.location.href = window.location.href;
                        } else {
                            if ($.helpers.settings.debug) {
                                alert(resp.Message);
                            } else {
                                $.helpers.log(resp.Message);
                            }
                        }
                    }
                }
                if (errorCallback) {
                    errorCallback(response);
                }
            }
        });
    };

    $.helpers.cancelEvent = function (e) {
        if (!e) {
            e = event;
        }
        if (!e) {
            return false;
        }
        if (e.stopPropagation) {
            e.stopPropagation();
        }
        if (e.preventDefault) {
            e.preventDefault();
        } else {
            e.cancelBubble = true;
            e.returnValue = false;
        }

        return false;
    };

    $.helpers.log = function (message) {
        if (console && console.log) {
            console.log(message);
        }
    };

    $.helpers.flash = function (el) {
        el.animate({ opacity: 0.1 }, 500)
            .animate({ opacity: 1 }, 500);
    };

    $.helpers.resolveUrl = function (relative) {
        var resolved = relative;
        if (relative[0] == '~') resolved = $.helpers.settings.relativeRoot + relative.substring(2);
        return resolved;
    };

    $.helpers.includeScript = function (url) {
        var script = document.createElement('script');
        script.type = 'text/javascript';
        script.src = url;
        $("head").append(script);
    };

    $.helpers.facebookConfirm = function (e, url) {
        FB.getLoginStatus(function (response) {
            function getFbUser() {
                FB.api('/me?fields=id,email', function (resp) {
                    var id = null, email = null;
                    if (resp) {
                        id = resp.id;
                        email = resp.email;
                    }

                    $.helpers.loadAsync(null, url, { facebookId: id, email: email }, function (result) {
                        if (result && result.error) {
                            if (confirm(result.error + JavaScriptLibraryResources.SomeoneElseConnectedToFb)) {
                                $.helpers.loadAsync(null, url, { facebookId: id, email: email, replace: true }, function (res) {
                                    if (res && res.success && e) {
                                        $(e.target).parents('.buttons').hide();
                                    }
                                });
                            }
                        }
                        if (result && result.success && e) {
                            $(e.target).parents('.buttons').hide();
                        }
                    });
                });
            }

            if (response.status === 'connected' && response.authResponse) {
                getFbUser();
            } else {
                $.helpers.facebookLogin(getFbUser, false, true);
            }
        });
    };

    $.helpers.facebookStatusCheck = function (url) {
        FB.getLoginStatus(function (response) {
            if (response.status === 'connected' && response.authResponse) {
                var str = '/me?fields=id';
                FB.api(str, function (resp) {
                    if (resp) {
                        setStatus(resp.id, true);
                    }
                    else {
                        setStatus(null, false);
                    }
                });
            }
            else {
                setStatus(null, false);
            }
        });

        function setStatus(id, connected) {
            $.helpers.loadAsync(null, url, { facebookId: id, isConnected: connected }, function (result) {
                $.helpers.log("Facebook status set: id:" + id + ", connected:" + connected);
            });
        }
    };

    $.helpers.scrollToElement = function(selector, time, verticalOffset) {
        time = typeof(time) != 'undefined' ? time : 1000;
        verticalOffset = typeof(verticalOffset) != 'undefined' ? verticalOffset : 0;
        var element = $(selector);
        var offset = element.offset();
        var offsetTop = offset.top + verticalOffset;
        $('html, body').animate({
            scrollTop: offsetTop
        }, time);
    };

    //    $.helpers.facebookRegister = function (url, lt2FbPageId, setFacebookFailedUrl) {
    //        FB.getLoginStatus(function (response) {
    //                function getFbUser() {
    //                    FB.api('/me?fields=id,first_name,last_name,username,likes,email', function(resp) {
    //                        if (resp) {
    //                            var isPageLiked = false;
    //                            for (i = 0; i < resp.likes.data.length; i++) {
    //                                if (resp.likes.data[i].id == lt2FbPageId) {
    //                                    isPageLiked = true;
    //                                    break;
    //                                }
    //                            }

    //                            $.postGo(url, {
    //                                'OAuthLogin.FacebookId': resp.id,
    //                                'OAuthLogin.FirstName': resp.first_name,
    //                                'OAuthLogin.LastName': resp.last_name,
    //                                'OAuthLogin.UserName': resp.username,
    //                                'OAuthLogin.Email': resp.email,
    //                                'OAuthLogin.IsPageLiked': isPageLiked,
    //                                ReturnTo: window.location.href
    //                            });
    //                        }
    //                    });
    //                }
    //            
    //                if (response.status === 'connected' && response.authResponse) {
    //                    $('#dialog-Facebook').dialog({
    //                        resizable: false,
    //                        height: 100,
    //                        width: 320,
    //                        modal: true,
    //                        autoOpen: true
    //                    });
    //                    getFbUser();
    //                } else{
    //                    if(response.status === 'not_authorized') {
    //                        $.helpers.facebookLogin(getFbUser);
    //                    }
    //                    
    //                    $.helpers.loadAsync(null, setFacebookFailedUrl, { failed: true });
    //                }
    //            });
    //    };
    var fbLoginAttached = false;
    $.helpers.facebookLogin = function (callback, needPostPersmission, forceLogin) {
        var rights = 'email';
        if (needPostPersmission) {
            rights += ',publish_stream';
        }

        function doLogin() {
            FB.login(function (resp) {
                if (resp.authResponse) {
                    callback(resp);
                } else {
                    $("#dialog-Facebook").dialog("close");
                    //attachLoginEvent();
                    $.helpers.log('User cancelled login or did not fully authorize.');
                }
            }, { scope: rights });
        }
        if (forceLogin) {
            doLogin();
        }
        else {
            FB.getLoginStatus(function (response) {
                if (response.status === 'connected') {
                    callback(response);
                }
                else if (response.status === 'not_authorized') {
                    doLogin();
                    //attachLoginEvent();
                }
                else {
                    doLogin();
                }
            });
        }

        function attachLoginEvent() {
            function attach(e) {
                if (e.which == 1) {
                    doLogin();
                    $(document).off('click', attach);
                    return $.helpers.cancelEvent(e);
                }
            }
            if (!fbLoginAttached) {
                $(document).on('click', attach);
                fbLoginAttached = true;
            }
        }
    };

    $.helpers.postToFacebook = function (message, url, forceLogin, noDetails, onePerDay) {
        if (typeof onePerDay == "undefined") {
            onePerDay = true;
        }
        var img = $("meta[property='og:image']").attr("content");
        $.helpers.facebookLogin(doPost, true, forceLogin);
        var tryCount = 0;
        function doPost() {
            var opts = {
                message: message,
                link: url ? url : location.href,
                picture: img,
                description: $('#spanVersionText').text(),
                name: window.title,
                caption: 'www.lietuva2.lt'
            };
            if (noDetails === true) {
                opts = {
                    message: message
                };
                
                if (url) {
                    opts.link = url;
                }
            }
            FB.api('/me/feed', 'post', opts, function (response) {
                if (!response || response.error) {
                    if (response && response.error && tryCount == 0) {
                        tryCount++;

                        //doPost();
                        //                        $(document).on('click', function(e) {
                        //                            if(e.which == 1) {
                        //                                $.helpers.facebookLogin(doPost, true, true);
                        //                            }
                        //                        });

                        $.helpers.loadAsync(null, $.helpers.settings.setFacebookPermissionGrantedUrl, { isGranted: false });
                    }
                    else {
                        $.helpers.log('Error occured: ' + response.error.message);
                    }
                }
                else {
                    if (onePerDay) {
                        $.helpers.loadAsync(null, $.helpers.settings.setPostedToFacebookUrl);
                    } else {
                        $.helpers.loadAsync(null, $.helpers.settings.setFacebookPermissionGrantedUrl, { isGranted: true });
                    }
                }
            });
        }
    };

    $.helpers.querystring = function (key, url) {
        if (!url) {
            url = document.location.search;
        }
        var re = new RegExp('(?:\\?|&)' + key + '=(.*?)(?=&|$)', 'gi');
        var r = [], m;
        while ((m = re.exec(url)) != null) r.push(m[1]);
        return r;
    };

    $.helpers.handleUniqueUserError = function (result) {
        if (result.Error.indexOf('UserNotUniqueException') >= 0) {
            $.helpers.identifyUser();
        }
        else if (result.Error.indexOf('UserNotUniqueViispException') >= 0) {
            $.helpers.identifyUserViisp();
        }
        else if (result.Error.indexOf('UnauthorizedAccessException') >= 0) {
            $.helpers.openLoginForm();
        } else if (result.Error.indexOf('AdditionalUniqueInfoRequiredException') >= 0) {
            $.helpers.openAdditionalUniqueInfoForm(result);
        } else if (result.Error.indexOf('PersonCodeNotConfirmedException') >= 0) {
            $.helpers.openPersonCodeConfirmationForm(result.Url);
        } else {
            alert(result.Error);
        }
    };

    $.helpers.handleUnauthorizedError = function (response) {
        if (response) {
            var ex = response;
            if (getError(ex)) {
                return true;
            }
            ex = $.parseJSON(response.responseText);
            if (getError(ex)) {
                return true;
            }
        }

        return false;
    };

    function getError(ex) {
        if (ex && ex.Message && ex.Message.indexOf('UnauthorizedAccessException') == 0) {
            $.helpers.openLoginForm();
            return true;
        }
        if (ex && ex.Message && ex.Message.indexOf('ConfirmEmail:') == 0) {
            alert(ex.Message);
            return true;
        }

        if (ex && ex.Message && ex.Message.indexOf('SignRequired:') == 0) {
            window.location = ex.Url;
            return true;
        }
    }

    $.helpers.openAdditionalUniqueInfoForm = function (result) {
        if (result && result.Content) {
            $('#divDialogContent').html(result.Content);
            if (result.Title) {
                $('#dialog').parent().find('.ui-dialog-title').text(result.Title);
            }
            initTooltips();
            $('#dialog').dialog({
                resizable: false,
                autoOpen: true,
                width: 650,
                height: 'auto',
                buttons: []
            });
        } else {
            var dialog = $('#dialog-additional-info');
            if (result) {
                dialog.find('#AddressLine').val(result.AddressLine);
                dialog.find('#DocumentNo').val(result.DocumentNo);
                dialog.find('#City').val(result.City);
                dialog.find('#Municipality').val(result.Municipality);
                dialog.find('#Country').val(result.Country);
                dialog.find('#DocumentNoRequired').val(result.DocumentNoRequired);
                dialog.find('#documentNoContainer').toggle(result.DocumentNoRequired);
                dialog.find('#msgReferenduum').toggle(result.DocumentNoRequired === true);
                dialog.find('#msgLaw').toggle(result.DocumentNoRequired === false);
                dialog.find('#VotesArePublic').toggle(result.VotesArePublic === true);
            }

            dialog.dialog({
                resizable: false,
                autoOpen: true,
                width: 600,
                height: 'auto'
            });
        }
    };

    $.helpers.openLoginForm = function () {
        var height = $.helpers.settings.loginDialogHeight;
        var width = $.helpers.settings.loginDialogWidth;

        $($.helpers.settings.dialogSelector).dialog({
            resizable: false,
            height: height,
            width: width,
            modal: true,
            autoOpen: false,
            buttons: []
        });

        $($.helpers.settings.dialogSelector).parent().find('.ui-dialog-title').text("Reikalingas prisijungimas");
        loadAsync(null, $.helpers.settings.loginPopupUrl + "?returnUrl=" + escape(window.location.href),
            null,
            function (result) {
                $($.helpers.settings.dialogContentSelector).html(result.Content);
                $($.helpers.settings.dialogSelector).dialog("open");
            });
    };

    $.helpers.openPersonCodeConfirmationForm = function (url) {

        $($.helpers.settings.dialogSelector).dialog({
            resizable: false,
            modal: true,
            autoOpen: false,
            height: 'auto',
            buttons: []
        });

        $($.helpers.settings.dialogSelector).parent().find('.ui-dialog-title').text("Patvirtinkite asmens kodą");
        loadAsync(null, $.helpers.settings.confirmPersonCodeUrl + "?returnUrl=" + escape(window.location.href),
            null,
            function (result) {
                $($.helpers.settings.dialogContentSelector).html(result.Content);
                $($.helpers.settings.dialogSelector).dialog("open");
                $('#formConfirmPersonCode').ajaxForm({
                    success: function (res) {
                        if (res.success === true) {
                            location.href = url;
                            //$($.helpers.settings.dialogSelector).dialog('close');
                            return;
                        }

                        if (res.Content) {
                            $('#formPersonCodeContent').html($(res.Content).find('#formPersonCodeContent'));
                        }
                    }
                });
            });
    };

    $.helpers.format = function () {
        var s = arguments[0];
        for (var i = 0; i < arguments.length - 1; i++) {
            var reg = new RegExp("\\{" + i + "\\}", "gm");
            s = s.replace(reg, arguments[i + 1]);
        }

        return s;
    };
    
    // function to check for an empty object
    $.helpers.isEmpty = function(obj) {
        for (var prop in obj) {
            if (obj.hasOwnProperty(prop))
                return false;
        }

        return true;
    };

})(jQuery);

//richtext editor
(function ($) {
    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.richtext.defaults, options);
            if (typeof this.ckeditor == 'function') {
                this.ckeditor(function() {
                }, opts);
            }
        }
    };

    $.fn.richtext = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.richtext.defaults = {
    };
})(jQuery);

//autocomplete
(function ($) {
    var methods = {
        init: function (url, options) {
            var opts = $.extend({}, $.fn.customautocomplete.defaults, options),
                hasChanged = false,
                selected = false,
                that = this;

            function requestAutoCompletion(request, response) {
                var params = {
                    timestamp: +new Date()
                };
                if (opts.parentControlSelectors) {
                    $.each(opts.parentControlSelectors, function (key, param) {
                        params[key] = param().val();
                    });
                }

                params[opts.paramName] = request.term;
                params["__RequestVerificationToken"] = $('[name=__RequestVerificationToken]').val();

                $.ajax({
                    url: url,
                    type: "POST",
                    data: params,
                    dataType: "json",
                    success: function (data) {
                        if (hasChanged && opts.mustMatch && (data == null || data.length == 0)) {
                            that.val('');
                            hasChanged = false;
                            return;
                        }
                        if (response) {
                            response(data);
                        }
                    }
                });
            }


            return this.each(function () {
                var $this = $(this), container = $this.parents(opts.containerSelector);
                if (container.length == 0) {
                    container = $(document);
                }
                $this.autocomplete({
                    source: requestAutoCompletion,
                    select: function (event, ui) {
                        if (opts.idFieldSelector) {
                            if (!ui.item.id) {
                                alert('No id returned!');
                            }
                            container.find(opts.idFieldSelector).val(ui.item.id);
                        }

                        if (opts.parentControlSelectors) {
                            var elements = ui.item.label.split(',');

                            var index = 1;
                            $.each(opts.parentControlSelectors, function (key, param) {
                                param().val($.trim(elements[index++]));
                            });
                        }
                        selected = true;
                    },
                    close: function (event, ui) {
                        if (opts.selectCallBack && selected) {
                            opts.selectCallBack($this);
                            selected = false;
                        }
                    },
                    minLength: 1
                }).change(function (e) {
                    if (opts.mustMatch) {
                        var request = new Object();
                        $(e.target).val($.trim($(e.target).val()));
                        request.term = $(e.target).val();
                        requestAutoCompletion(request);
                        hasChanged = true;
                    }
                });
            });
        }
    };

    $.fn.customautocomplete = function (method) {

        return methods.init.apply(this, arguments);
        //        if (methods[method]) {
        //            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        //        } else {
        //            return methods.init.apply(this, arguments);
        //        }
    };

    $.fn.customautocomplete.defaults = {
        idFieldSelector: null,
        parentControlSelectors: null,
        selectCallBack: null,
        mustMatch: false,
        paramName: 'prefix',
        containerSelector: '[data-role="container"]'
    };
})(jQuery);

//remote title call
(function ($) {
    $.fn.remote = function (url, options) {
        var opts = $.extend({}, $.fn.remote.defaults, options);
        this.each(function () {
            var $this = $(this);
            $this.on('change', opts.textboxSelector, function (ev) {
                var btn = $this.find(opts.buttonToDisableSelector);
                btn.attr('disabled', 'disabled');

                $.helpers.loadAsync(ev, url, { url: $(ev.target).val() }, function (title) {
                    if (title == "") {
                        $(ev.target).val('');
                    } else {
                        $(ev.target).parents(opts.containerSelector).find(opts.controlToUpdateSelector).val(title);
                    }

                    btn.removeAttr('disabled');
                });
            });
        });
    };

    $.fn.remote.defaults = {
        buttonToDisableSelector: 'input.save',
        controlToUpdateSelector: 'input.urltitle',
        textboxSelector: '.remoteurl',
        containerSelector: '.fields_container'
    };
})(jQuery);

//jquery expandable text plugin
(function ($) {
    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.expandable.defaults, options);

            return this.each(function () {
                var container = $(this);
                var linkExpand = container.find(opts.linkSelector);
                if (linkExpand.length > 0) {
                    linkExpand.click(function (e) {
                        container.addClass(opts.showClass);
                    });
                }
            });
        }
    };

    $.fn.expandable = function (method) {
        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.expandable.defaults = {
        linkSelector: '.text_expanded_link',
        showMoreText: JavaScriptLibraryResources.ShowMore,
        showClass: 'text_expanded',
        containerSelector: '.text_expandable',
        container: $(document)
    };
})(jQuery);

//jquery show more link plugin
(function ($) {
    $.fn.showmore = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.showmore.defaults = {
        linkSelector: "#lnkShowMore",
        listSelector: "#list",
        containerSelector: null,
        href: null,
        data: {},
        callback: function (html) {
            $('div.text_expandable', html).expandable();
        },
        append: true,
        prepend: false
    };

    var boundElements = new Array();

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.showmore.defaults, options);
            if (opts.prepend) {
                opts.append = false;
            }

            if (boundElements.indexOf(opts.linkSelector) == -1) {
                $(document).on('click', opts.linkSelector, requestUpdate);
                boundElements.push(opts.linkSelector);
            }

            function requestUpdate(e) {
                var link = $(e.target);
                var pageNumber = link.data('pageNumber') ? link.data('pageNumber') : 1;
                var data = $.extend({}, opts.data, { pageIndex: pageNumber });
                var href = opts.href;
                if (link.attr('href')) {
                    href = link.attr('href');
                }
                $.helpers.loadAsync(e, href, data,
                    function (result) {
                        if (!result.HasMoreElements) {
                            link.hide();
                        } else {
                            link.show();
                            link.data('pageNumber', ++pageNumber);
                        }

                        if (!result.Content) {
                            return;
                        }
                        var el, list;
                        if (opts.containerSelector) {
                            list = link.parents(opts.containerSelector).find(opts.listSelector);
                        }
                        else {
                            list = $(opts.listSelector);
                        }

                        if (opts.append) {
                            el = $(result.Content).appendTo(list);
                        } else if (opts.prepend) {
                            el = $(result.Content).prependTo(list);
                        } else {
                            list.html(result.Content);
                            el = list.html();
                        }

                        if (typeof (opts.callback) == 'function') opts.callback(el);
                    }, true, true);

                return $.helpers.cancelEvent(e);
            }
        },
        reset: function (index) {
            index = $.helpers.checkDefault(index, 0);
            return this.data('pageNumber', index);
        },

        update: function (url, hasMore) {
            var $this = this;
            return this.each(function () {
                var opts = $(this).data('options');
                var link = opts.container.find(opts.linkSelector);
                link.attr('href', url);
                if (hasMore === false) {
                    link.hide();
                }
                else {
                    link.show();

                    $this.showmore('reset', 1);
                }
            });
        }
    };


})(jQuery);

//jquery multi selector (dropdownchecklist) wrapper
(function ($) {

    var updatedHash;

    $.fn.multiselector = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.multiselector.defaults = {
        filterPageUrl: null,
        saveUrl: null,
        allItemsText: JavaScriptLibraryResources.AllCategories,
        itemCountText: JavaScriptLibraryResources.NumberOfCategories,
        totalCountSelector: "#spanTotalCount",
        linkShowMoreSelector: "#lnkShowMore",
        categoryTagSelector: null,
        listSelector: "#list",
        expandableSelector: "div.text_expandable",
        saveMessageSelector: "#spanMessage",
        saveMessage: JavaScriptLibraryResources.CategoriesSaved,
        invertButtonId: null,
        saveButtonId: null,
        revertButtonId: null,
        selectText: JavaScriptLibraryResources.SelectCategory
    };

    var methods = {
        init: function (customoptions) {
            var opts = $.extend({}, $.fn.multiselector.defaults, customoptions);

            if (!opts.filterPageUrl) {
                throw 'filterPageUrl must be specified';
            }
            var $this = this;
            var form = this.parents('form:first');
            this.dropdownchecklist({
                icon: {},
                width: 170,
                maxDropHeight: 200,
                firstItemChecksAll: true,
                textFormatFunction: function (selectoptions) {
                    var selectedOptions = selectoptions.filter(":selected");
                    var countOfSelected = selectedOptions.size();
                    var size = selectoptions.size();
                    switch (countOfSelected) {
                        case 0:
                            return opts.selectText;
                        case 1:
                            return selectedOptions.text();
                        case size:
                            return opts.allItemsText;
                        default:
                            return opts.itemCountText + countOfSelected;
                    }
                },
                onComplete: updateList
            });

            if (opts.invertButtonId) {
                $(opts.invertButtonId).click(invertSelection);
            }
            if (opts.saveButtonId) {
                $(opts.saveButtonId).click(save);
            }
            
            if (opts.categoryTagSelector) {
                $(opts.listSelector).on('click', opts.categoryTagSelector, selectByValue);
            }

            if (window.location.hash.indexOf("filter") > 0) {
                updateList();
            }

            function updateList(e) {
                $(opts.saveButtonId).show();
                $(opts.revertButtonId).show();

                var hash = hex_md5(form.serialize());
                if (updatedHash != hash) {
                    window.location.hash = "filter_" + hash;
                    updatedHash = hash;
                }
                else {
                    return;
                }


                loadAsync(e, opts.filterPageUrl,
                    form.serialize(),
                    function (result) {
                        var list = $(opts.listSelector);
                        list.html(result.Content);
                        $(opts.expandableSelector, list).expandable();

                        var link = $(opts.linkShowMoreSelector);
                        if (!result.HasMoreElements) {
                            link.hide();
                        } else {
                            link.show();
                        }

                        link.showmore('reset');

                        if ($(opts.totalCountSelector)) {
                            $(opts.totalCountSelector).text(result.TotalCount);
                        }
                    });
            }

            function save(e) {
                loadAsync(e, opts.saveUrl,
                    form.serialize(),
                    function (result) {
                        if (result) {
                            $(opts.saveMessageSelector).text(opts.saveMessage);
                        }
                    });

                return cancelEvent(e);
            }

            function invertSelection(e) {
                var options = $this.children().filter('option');
                var selectedOptions = options.filter(":selected");
                options.each(function (index, el) {
                    var anItem = $(this);
                    if (index == 0) {
                        if (selectedOptions.size() == 0) {
                            anItem.attr("selected", true);
                        }
                        if (options.size() == selectedOptions.size()) {
                            anItem.removeAttr("selected");
                        }

                        return;
                    }

                    if (anItem.attr("selected")) {
                        anItem.removeAttr("selected");
                    } else {
                        anItem.attr("selected", true);
                    }
                });

                $this.dropdownchecklist("refresh");
                updateList(e);
                return cancelEvent(e);
            }
            
            function selectByValue(e) {
                var options = $this.children();
                var valueToSelect = $(e.target).data("id");
                options.each(function (index, el) {
                    if (index == 0) {
                        $(el).removeAttr("selected");

                        return;
                    }
                    if (el.value == valueToSelect) {
                        $(el).attr("selected", "selected");
                    } else {
                        $(el).removeAttr("selected");
                    }
                });

                $this.dropdownchecklist("refresh");
                updateList(e);
                return cancelEvent(e);
            }
        }
    };
})(jQuery);

//jquery customized dialog plugin
(function ($) {
    $.fn.linkdialog = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.linkdialog.defaults = {
        dialogSelector: '#dialog',
        buttons: [],
        closeText: JavaScriptLibraryResources.Close,
        openCallback: function () {
        },
        closeCallback: function () {
        },
        headerText: null,
        isAjax: false,
        url: null,
        data: null,
        width: 'auto',
        height: 'auto',
        addCloseButton: true
    };

    var methods = {
        init: function (options) {
            var opts = $.extend(true, {}, $.fn.linkdialog.defaults, options);
            if (opts.addCloseButton) {
                opts.buttons.push({
                    text: opts.closeText,
                    click: function () {
                        $(this).dialog("close");
                        opts.closeCallback();
                    }
                });
            }

            var dialog = opts.dialogSelector;
            if (typeof (dialog) == "string") {
                dialog = $(dialog);
            }

            dialog.dialog(opts);


            $(document).on('click', this.selector, function (e) {
                var headerText = opts.headerText;
                if (!headerText) {
                    headerText = $(this).attr('data-title');
                }

                if (headerText) {
                    dialog.parent().find('.ui-dialog-title').text(headerText);
                }

                if (opts.isAjax) {

                    var url = opts.url;
                    if (!url) {
                        url = $(this).attr('href');
                    }

                    $.helpers.loadAsync(e, url,
                        opts.data,
                        function (result) {
                            var ret = opts.openCallback(result);
                            if (ret === false) {
                                return;
                            }
                            
                            dialog.dialog(opts);
                            dialog.dialog("open");
                            dialog.scrollTop(0);
                        });
                }
                else {
                    opts.openCallback();
                    dialog.dialog(opts);
                    dialog.dialog("open");
                    dialog.scrollTop(0);
                }

                cancelEvent(e);
            });
        }
    };
})(jQuery);


//jquery plugin template
(function ($) {
    $.fn.deleteButton = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.deleteButton.defaults = {
        deleteSelector: '[data-role="delete"]',
        containerSelector: '[data-role="deleteContainer"]:first',
        imgDeleteSrc: null,
        imgDeleteHighlightSrc: null
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.deleteButton.defaults, options);
            var imgDeleteSrc = opts.imgDeleteSrc, imgDeleteHighlightSrc = opts.imgDeleteHighlightSrc;

            getImageUrls();

            function getImageUrls() {
                if (!imgDeleteSrc) {
                    imgDeleteSrc = $(opts.deleteSelector).find('img').attr('src');
                    if (imgDeleteSrc) {
                        var split = imgDeleteSrc.split('.');
                        imgDeleteHighlightSrc = split[0] + "_highlight." + split[1];
                    }
                }
            }

            return this.each(function () {
                var $this = $(this);

                $this.on('mouseenter', '*', function (e) {
                    $this.find(opts.deleteSelector).hide();

                    $(e.target).parents(opts.containerSelector).find(opts.deleteSelector + ":first").show();
                });

                $this.on('mouseleave', function (e) {
                    $this.find(opts.deleteSelector).hide();
                });

                $this.on('mouseenter', opts.deleteSelector, function (e) {
                    getImageUrls();
                    $(this).find('img').attr('src', imgDeleteHighlightSrc);
                });

                $this.on('mouseleave', opts.deleteSelector, function (e) {
                    getImageUrls();
                    $(this).find('img').attr('src', imgDeleteSrc);
                });

                $this.on('click', opts.deleteSelector, function (e) {
                    if (!confirm(JavaScriptLibraryResources.ConfirmDelete)) {
                        return $.helpers.cancelEvent(e);
                    }
                    var $link = $(this).find('a');
                    if (!$link.attr('href') || $link.attr('href').indexOf('void') >= 0) {
                        var container = $(this);
                        if (!container.is(opts.containerSelector)) {
                            container = container.parents(opts.containerSelector);
                        }
                        if (container.length > 0) {
                            container.remove();
                            return false;
                        }

                        return true;
                    }
                    $.helpers.loadAsync(e, $link.attr('href'), null, function (result) {
                        if (result) {
                            $link.parents(opts.containerSelector).slideUp();
                        }
                        else {
                            alert(JavaScriptLibraryResources.CannotDelete);
                        }

                    });
                    return $.helpers.cancelEvent(e);
                }
                );
            });
        }
    };
})(jQuery);

//jquery plugin template
(function ($) {
    $.fn.confirmDelete = function () {

        this.on('click', function (e) {
            if (!confirm(JavaScriptLibraryResources.ConfirmDelete)) {
                return $.helpers.cancelEvent(e);
            }
        });
    };

    $.fn.confirmDelete.defaults = {

    };

})(jQuery);

(function ($) {
    $.fn.ajaxTabs = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.ajaxTabs.defaults = {
        tabItemSelector: '.tabitem',
        data: null,
        highlightClass: 'highlight',
        callback: function () {
        },
        deselectUrl: null
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.ajaxTabs.defaults, options);
            this.find(opts.tabItemSelector).click(switchTab);
            var $this = this;

            function switchTab(e) {
                var el = $(this);
                var url = el.attr('data-url') ? el.attr('data-url') : el.attr('href');
                var deselect = false;
                if (opts.deselectUrl && el.hasClass(opts.highlightClass)) {
                    url = opts.deselectUrl;
                    deselect = true;
                }

                $.helpers.loadAsync(e, url,
                    opts.data,
                    function (result) {
                        $this.find(opts.tabItemSelector).removeClass(opts.highlightClass);
                        if (!deselect) {
                            el.addClass(opts.highlightClass);
                        }

                        opts.callback(result);
                    });
                return $.helpers.cancelEvent(e);
            }
        }
    };
})(jQuery);

//jquery plugin template
(function ($) {
    $.fn.googleDocs = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.googleDocs.defaults = {
        titleSelector: '#VersionSubject',
        appId: '',
        formSelector: 'form#frmIdea',
        showPrompt: false,
        saveTempDataUrl: null
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.googleDocs.defaults, options);

            var changed = false;

            if (opts.showPrompt) {
                $(opts.formSelector).find('input, textarea').on('change', function (e) {
                    changed = true;
                });

                if (typeof (CKEDITOR) != "undefined" && CKEDITOR.instances) {
                    for (var instance in CKEDITOR.instances) {
                        CKEDITOR.instances[instance].on('change', function () {
                            changed = true;
                        });
                    }
                }
            }

            $('#lnkGoogleAuth').on('click', function (e) {
                var url = $(this).attr('href');
                function auth(ev) {
                    $.helpers.loadAsync(ev, url, function () { return $(opts.formSelector).serialize(); }, function (result) {
                        if (result.RedirectUrl) {
                            if (opts.showPrompt && changed) {
                                if (!confirm(JavaScriptLibraryResources.GoogleConnectConfirm)) {
                                    changed = false;
                                    return false;
                                }
                            }
                            window.location = result.RedirectUrl;
                            return;
                        }
                    });
                }

                if (opts.saveTempDataUrl) {
                    $.helpers.loadAsync(e, opts.saveTempDataUrl, $(opts.formSelector).serialize(), function (result) {
                        auth(e);
                    });
                }
                else {
                    auth(e);
                }

                return $.helpers.cancelEvent(e);
            });

            $('#lnkCreateDoc').hijack(function (result) {
                if (result.error) {
                    if (result.error == 'Unauthorized') {
                        $('#lnkGoogleAuth').click();
                    } else {
                        alert(result.error);
                    }
                }
                var link = $(result.Content).appendTo($('#listAttachments'));
                $('#lnkOpenDoc').attr('href', link.find('a.attachment').attr('href')).show();
                $('#lnkCreateDoc').hide();
            }, { data: function () { return { subject: $(opts.titleSelector).val() }; } });


            $('#lnkSelectDoc').on('click', function (e) {
                var url = $(this).attr('href');
                $('.ui-dialog').css('zIndex', 1000);
                // The Browser API key obtained from the Google Developers Console.
                var developerKey = 'AIzaSyBuMbvzBPh8Jum-GrX2hRDzsnXR9oJpMXg';

                // The Client ID obtained from the Google Developers Console. Replace with your own Client ID.
                var clientId = opts.appId;

                // Scope to use to access user's photos.
                var scope = ['https://www.googleapis.com/auth/drive'];

                var pickerApiLoaded = false;
                var oauthToken;

                // Use the API Loader script to load google.picker and gapi.auth.
                gapi.load('auth', { 'callback': onAuthApiLoad });
                gapi.load('picker', { 'callback': onPickerApiLoad });

                function onAuthApiLoad() {
                    gapi.auth.init();
                    window.gapi.auth.authorize(
                        {
                            'client_id': clientId,
                            'scope': scope,
                            'immediate': false
                        },
                        handleAuthResult);
                }

                function onPickerApiLoad() {
                    pickerApiLoaded = true;
                    createPicker();
                }

                function handleAuthResult(authResult) {
                    if (authResult && !authResult.error) {
                        oauthToken = authResult.access_token;
                        createPicker();
                    }
                }

                function createPicker() {
                    if (pickerApiLoaded && oauthToken) {
                        var picker = new google.picker.PickerBuilder()
                            .enableFeature(google.picker.Feature.MULTISELECT_ENABLED)
                            .enableFeature(google.picker.Feature.MINE_ONLY)
                            .addView(google.picker.ViewId.DOCS)
                            .addView(new google.picker.DocsUploadView())
                            .setOAuthToken(oauthToken)
                            .setDeveloperKey(developerKey)
                            .setCallback(function (data) {
                                if (data.action == 'picked') {
                                    var o = {};
                                    $.each(data.docs, function (index) {
                                        //o.docs.push({Url: this.url, Title: this.name});
                                        o["docs[" + index + "].Url"] = this.url;
                                        o["docs[" + index + "].Title"] = this.name;
                                        o["docs[" + index + "].IconUrl"] = this.iconUrl;
                                        o["docs[" + index + "].Id"] = this.id;
                                    });
                                    $.helpers.loadAsync(null, url, o, function (result) {
                                        $('#listAttachments').append(result.Content);
                                    });
                                }
                            })
                            .build();
                        picker.setVisible(true);
                    }
                }

                return $.helpers.cancelEvent(e);
            });
        },
        reset: function () {
            $('#lnkOpenDoc').hide();
            $('#lnkCreateDoc').show();
        }
    };
})(jQuery);

(function ($) {
    $.fn.hijack = function (callback, options) {
        var opts = $.extend({}, $.fn.hijack.defaults, options);
        return this.each(function () {
            var $this = $(this);
            
            $this.on('click', function (e) {
                if (opts.confirmMessage) {
                    if (!confirm(opts.confirmMessage)) {
                        return $.helpers.cancelEvent(e);
                    }
                }
                $.helpers.loadAsync(e, $this.attr('href'), opts.data, function (result) {
                    callback.call($this, result);
                });

                return $.helpers.cancelEvent(e);
            });
        });
    };

    $.fn.hijack.defaults = {
        data: null,
        confirmMessage: null
    };
})(jQuery);

//jquery plugin template
(function ($) {
    $.fn.basictemplate = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.basictemplate.defaults = {
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.basictemplate.defaults, options);
        }
    };
})(jQuery);

//common initializers
(function ($) {
    $(function () {
        $(document).on('click', 'a[data-hijack]', function (e) {
            $.helpers.loadAsync(e, $(this).attr('href'), null, eval($(this).attr('data-hijack')));
            return $.helpers.cancelEvent(e);
        });

        if (typeof createExpandableText == 'function') {
            createExpandableText();
        }

        $.extend($.ui.dialog.prototype.options, {
            modal: true,
            resizable: false,
            autoOpen: false,
            width: 'auto',
            height: 'auto',
            closeOnEscape: true
        });

        if ($.validator) {
            $.validator.methods.date = function (value, element) {
                var date = value.replace(/-/g, '/');
                return this.optional(element) || ! /Invalid|NaN/.test(new Date(date));
            };
        }

        if (typeof $.fn.autosize == 'function') {
            $('textarea').autosize();
        }
        
        $(document).on('click', '.ui-widget-overlay', function () {
            var close = $(".ui-dialog-titlebar-close:visible");
            if (close.length == 1) {
                close.trigger('click');
            } else {
                var best;
                var maxz;
                close.each(function () {
                    var z = parseInt($(this).css('z-index'), 10);
                    if (!best || maxz < z) {
                        best = this;
                        maxz = z;
                    }
                });
                $(best).trigger('click');
            }
        });

        $('.share_url').on('click', function(e) {
            $(this).select();
        });

        if (jQuery().UItoTop) {
            jQuery().UItoTop({scrollSpeed: 500});
        }
    });
})(jQuery);
if (!Array.prototype.indexOf) {
    Array.prototype.indexOf = function (elt /*, from*/) {
        var len = this.length;

        var from = Number(arguments[1]) || 0;
        from = (from < 0)
             ? Math.ceil(from)
             : Math.floor(from);
        if (from < 0)
            from += len;

        for (; from < len; from++) {
            if (from in this &&
                this[from] === elt)
                return from;
        }
        return -1;
    };
}