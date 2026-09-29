(function ($) {
    $.fn.todos = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    function getList(mileStoneId, isFinished, target) {
        var list;
        if (!isFinished) {
            if (mileStoneId) {
                var container = $('input:hidden[value="' + mileStoneId + '"]').parents('.mileStoneItemContainer');
                list = container.find('.toDos');

            } else {
                list = $('#listToDos');
                if (!list.is('.toDos')) {
                    list = list.children('.toDos');
                }
            }
        }
        else {
            var container = target.parents('.mileStoneItemContainer');
            if (mileStoneId) {
                list = container.find('.finishedToDos');
            }
            else {
                list = $('#listFinishedToDos');
                if (!list.is('.finishedToDos')) {
                    list = list.children('.finishedToDos');
                }
            }
        }

        return list;
    }

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.todos.defaults, options);

            var $this = this, form = $this.find(opts.inputFormSelector);
            form.ajaxForm(function (result) {
                if ($.helpers.handleUnauthorizedError(result)) {
                    return false;
                }
                var list = getList(result.MileStoneId);

                elem = $(result.Content).appendTo(list);
                form.find('textarea').val('');
            });

            $this.find(opts.listsContainerSelector).on('click', 'input.chkFinish', finishToDo);
            $this.find(opts.listsContainerSelector).on('click', 'a.imgDelete', deleteToDo);
            $this.find(opts.listsContainerSelector).on('click', 'a.lnkEditItem', getEditToDo);
            $this.find(opts.listsContainerSelector).on('click', 'a.lnkTakeItem', function (e) {
                var $this = $(this);
                $.helpers.loadAsync(e, $this.attr('href'), null, function (result) {
                    var container = $this.parents('.itemContainer');
                    container.html(result.Content);
                    container.removeClass('hover');
                });
                return $.helpers.cancelEvent(e);
            });

            bindHover();

            var startIndex = 0;
            $this.find('div[data-reorder-url]').sortable({ handle: '.dragHandle', axis: 'y',
                update: function (e, ui) {
                    var index = $(this).children('.itemContainer').index(ui.item);
                    var list = $(this);
                    var mileStoneId = list.find('.hiddenMileStoneId').val();
                    list.sortable("disable");
                    loadAsync(e, $(this).attr('data-reorder-url'),
                        function () {
                            return { startPos: startIndex, endPos: index, milestoneId: mileStoneId };
                        },
                        function (result) {
                            list.sortable("enable");
                        });
                }, start: function (e, ui) {
                    startIndex = $(this).children('.itemContainer').index(ui.item);
                }
            });
            $('[data-role="insert-todo"]').click(function (e) {
                $(this).hide().next().show();
            });
            $('[data-role="cancel-insert"]').click(function (e) {
                $(this).parents('.todo_form').hide().prev().show();
            });

            function bindHover() {
                var list = $this.find(opts.listsContainerSelector);
                list.on('mouseenter', '.itemContainer', function (e) {
                    list.find('.itemContainer').removeClass('hover');

                    $(this).addClass('hover');

                });

                list.on('mouseleave', function (e) {
                    list.find('.itemContainer').removeClass('hover');
                });
            }

            function finishToDo(e) {
                var item = $(e.target).parents('.itemContainer');
                //var id = item.find('.hiddenId').val();
                var container = item.parents('.mileStoneItemContainer');
                var mileStoneId = container.find('.hiddenMileStoneId').val();
                loadAsync(e, $(this).attr('data-url'),
                null,
                function (result) {
                    var list = getList(mileStoneId, result.IsFinished, $(e.target));

                    if (result.IsFinished) {
                        var elem = $(result.Content).prependTo(list);
                    }
                    else {
                        var elem = $(result.Content).appendTo(list);
                    }
                    $(e.target).parents('.itemContainer').remove();
                });
            }

            function deleteToDo(e) {
                if (confirm(JavaScriptLibraryResources.ConfirmDelete)) {
                    var url = $(e.target).attr('href');
                    if (!url) {
                        url = $(e.target).parent().attr('href');
                    }
                    loadAsync(e, url,
                null,
                function (result) {
                    if (result) {
                        $(e.target).parents('.itemContainer').remove();

                    }
                });
                }
                cancelEvent(e);
            }

            function getEditToDo(e) {
                var url = $(e.target).attr('href');
                if (!url) {
                    url = $(e.target).parent().attr('href');
                }
                loadAsync(e, url,
                null,
                function (result) {
                    var container = $(e.target).parents('.itemContainer').find('.editFormContainer');
                    container.html(result.Content);
                    $('#lnkCancel', container).click(function (e) {
                        container.empty();
                    });
                    var form = $('#formEditToDo', container);
                    jQuery.validator.unobtrusive.parse(form);
                    form.validate().settings.submitHandler = editToDo;
                    setupDatePicker();
                });
                cancelEvent(e);
            }


            function editToDo(form) {
                loadAsync(null, $(form).attr('action'),
                $(form).serialize(),
                function (result) {
                    var container = $(form).parents('.itemContainer');
                    container.html(result.Content);
                    container.removeClass('hover');
                });
                return cancelEvent(form);
            }
        }
    };

    $.fn.todos.defaults = {
        inputFormSelector: '.insertToDo form',
        editFormSelector: '#formEditToDo',
        listsContainerSelector: '#listMileStones, #listToDos, #listFinishedMileStones, #listFinishedToDos'
    };

})(jQuery);