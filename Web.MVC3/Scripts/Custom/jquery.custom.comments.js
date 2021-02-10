//jquery comment input plugin
(function ($) {
    $.fn.CommentsInput = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.CommentsInput.defaults = {
        requiredText: 'Įveskite komentarą',
        callback: function () {
        },
        data: null,
        listSelector: '#listComments',
        commentsListContainerSelector: '#divCommentListContainer',
        append: false
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.CommentsInput.defaults, options);
            return this.each(function () {
                var $this = $(this),
                    $form = $this.find('form:first'),
                    $list = opts.listSelector == null ? $this.find('ul:first') : $this.find(opts.listSelector);

                $form.ajaxForm({ success:
                            function (result) {
                                if ($.helpers.handleUnauthorizedError(result)) {
                                    return false;
                                }
                                
                                if (result.Error) {
                                    $.helpers.handleUniqueUserError(result);

                                    return false;
                                }
                                
                                var comments = $list;
                                var comment;
                                if (opts.append) {
                                    comment = $(result.Comment).appendTo(comments);
                                } else {
                                    comment = $(result.Comment).prependTo(comments);
                                }
                                $.helpers.flash(comment);
                                
                                if ($form.find('#chkPostToFacebook').is(':checked')) {
                                    var url = window.location.href;

                                    $.helpers.postToFacebook($form.find('textarea').val(), url, false, true, false);
                                }
                                
                                if (typeof ($.preview) == 'object') {
                                    for (i = 0; i < $.preview.instances.length; i++) {
                                        $.preview.instances[i].clearSelector();
                                    }
                                }
                                
                                $form.find('textarea').val('');
                                comment.find('textarea').val('');
                                createExpandableText(comment);
                                $this.find(opts.commentsListContainerSelector).show();
                                $this.find('.separatorPeriod').hide();
                                $this.find('.separatorClear').show();
                                if (typeof $.fn.autosize == 'function') {
                                    $this.find('textarea').autosize();
                                }

                                if (result.SubscribeMain) {
                                    $('.statistics_buttons [data-role="subscribe"]').parent().html(result.SubscribeMain);
                                    $this.parents('.problem_container').find('[data-role="subscribe"]').parent().html(result.SubscribeMain);
                                }

                                opts.callback(result, comment, $form);
                            },
                    data: opts.data
                });
                if (typeof ($.preview) == 'object') {
                    $form.find('textarea').preview({ key: $.helpers.settings.embedlyKey });
                }
            });
        },
        clear: function () {
            var $this = $(this),
                $form = $this.find('form:first');
            $form.ajaxFormUnbind();
        }
    };
})(jQuery);

//jquery comment plugin
(function ($) {
    $.fn.Comments = function(method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.Comments.defaults = {
    };

    var methods = {
        init: function(options) {
            var opts = $.extend({ }, $.fn.Comments.defaults, options);

            this.deleteButton();

            return this.each(function() {
                var $this = $(this);

                $this.on('click', 'a.commentacomment', function(e) {
                    var div = $(this).parents('.commentCommentsContainer').find('.comment_input').show();
                    var form = div.find('form');
                    if(typeof ($.preview) == 'object') {
                        form.find('textarea').preview({ key: $.helpers.settings.embedlyKey });
                    }
                    
                    $(this).hide();
                    form.find('textarea').focus();
                });

                $this.on('click', '[data-role="subscribe"]', function (e) {
                    var $el = $(this);
                    $.helpers.loadAsync(e, $el.attr('href'), null, function (result) {
                        $el.parent().html(result.Content);
                    });

                    return $.helpers.cancelEvent(e);
                });

                $this.on('click', 'a.cancel', function(e) {
                    var form = $(this).parents('form:first');
                    form.hide();
                    form.parent().find('.commentacomment').show();
                    return $.helpers.cancelEvent(e);
                });

                $this.on('click', 'a[data-role="show-hide-text"]', function(e) {
                    var el = $(this);
                    $.helpers.loadAsync(e, el.attr('href'), null, function(result) {
                        el.parents('[data-role="deleteContainer"]:first').html(result.Content);
                    });
                    return $.helpers.cancelEvent(e);
                });

                $this.on('submit', "form[data-role='commentcommentinput']", function(e) {
                    var $form = $(this), list = $form.parents('.commentCommentsContainer').find('.innerList:last');
                    $.helpers.loadAsync(e, $form.attr('action'), $form.serialize(), function(result) {
                        if (result.Error) {
                            $.helpers.handleUniqueUserError(result);

                            return false;
                        }
                        
                        var comment = $(result.Comment).appendTo(list);
                        $.helpers.flash(comment);
                        $form.find('textarea').val('');
                        if(typeof ($.preview) == 'object') {
                            for (i = 0; i < $.preview.instances.length; i++) {
                                $.preview.instances[i].clearSelector();
                            }
                        }
                        
                        $form.parents('.comment_input').hide();
                        var cont = $form.parents('.comment_input_container:first');
                        cont.addClass('coloredInnterList');
                        cont.find('.commentacomment').show();

                        createExpandableText(comment);
                        
                        if (result.SubscribeMain) {
                            $('.statistics_buttons [data-role="subscribe"]').parent().html(result.SubscribeMain);
                            $this.parents('.problem_container').find('[data-role="subscribe"]').parent().html(result.SubscribeMain);
                        }
                        
                        if (result.Subscribe) {
                            $form.parents('[data-role="deleteContainer"]').find('[data-role="subscribe-container"]').html(result.Subscribe);
                        }
                    });

                    return $.helpers.cancelEvent(e);
                });
            });
        }
    };

})(jQuery);

//jquery plugin template
(function($) {
    $.fn.liking = function(method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.liking.defaults = {
        
    };

    var methods = {
        init: function(options) {
            var opts = $.extend({ }, $.fn.liking.defaults, options);

            this.on('click', 'a.like', function(e) {
                $.helpers.loadAsync(e, $(this).attr('href'),
                    function() {
                        var ret = new Object();
                        var id = $(e.target).attr('entryId');
                        ret["commentId"] = $(e.target).attr('commentId');
                        if (id) {
                            ret["id"] = id;
                        }

                        ret["parentId"] = $(e.target).attr('parentId');

                        return ret;
                    },
                    function(result) {
                        if(result.Content) {
                            var container = $(e.target).parents('.likeContainer');
                            container.html(result.Content);
                        }
                    });
                return $.helpers.cancelEvent(e);
            });
            
            this.on('click', 'a.undoLike', function(e) {
                $.helpers.loadAsync(e, $(this).attr('href'),
                    function() {
                        var ret = new Object();
                        var id = $(e.target).attr('entryId');
                        ret["commentId"] = $(e.target).attr('commentId');
                        if (id) {
                            ret["id"] = id;
                        }

                        ret["parentId"] = $(e.target).attr('parentId');

                        return ret;
                    },
                    function(result) {
                        var container = $(e.target).parents('.likeContainer');
                        container.html(result.Content);
                    });
                
                return $.helpers.cancelEvent(e);
            });

            return this;
        }
    };
})(jQuery);