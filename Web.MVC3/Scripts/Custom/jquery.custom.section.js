(function ($) {
    $.fn.section = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.section.defaults = {

    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.section.defaults, options);

            return this.each(function () {
                var $this = $(this);

                $this.on('click', '.edit', function (e) {
                    loadAsync(e, $(e.target).attr('href'),
                        null,
                        function (result) {
                            var parent = $(e.target).parents('div.sectionContainer');
                            parent.html(result.Content);
                            setupDatePicker();
                            $.validator.unobtrusive.parse(parent.find("form"));
                            parent.find('textarea').each(function (i, el) {
                                var length = $(el).attr('maxtextlength');
                                if (!length) {
                                    length = 300;
                                }
                                $(el).supertextarea(
                                    {
                                        maxl: length,
                                        maxh: 500,
                                        maxw: 420,
                                        dsrm: { use: true, text: JavaScriptLibraryResources.SymbolsLeft, css: { 'color': '#666', 'font-size': '0.9em'} }
                                    });
                            });
                        });
                    return cancelEvent(e);
                });

                $this.on('submit', 'form', function (e) {
                    var form = $(e.target);
                    $.helpers.loadAsync(e, form.attr('action'),
                        form.serialize(),
                        function (result) {
                            var container = form.parents('div.sectionContainer');
                            container.html(result.Content);
                        });
                    return cancelEvent(e);
                });

                $this.on('click', 'a.cancel', function (e) {
                    var url = $(e.target).attr('href');
                    $.helpers.loadAsync(e, url,
                        null,
                        function (result) {
                            var container = $(e.target).parents('div.sectionContainer');
                            container.html(result);
                        });
                    return cancelEvent(e);
                });

                $this.on('click', '.add', function (e) {
                    var el = $(this);
                    var url = el.attr('href');
                    loadAsync(e, url, null, function (result) {
                        var parent = el.parents('[data-role="parent-container"]').find('.itemsContainer');
                        parent.append(result.Content);
                        var form = el.parents('div.sectionContainer').find("form");
                        if (form.length == 0) {
                            form = el.closest('form');
                        }
                        if (form.length > 0) {
                            $.data(form[0], 'validator', null);
                            $.validator.unobtrusive.parse(form);
                        }
                        if (result.UpdatedHref) {
                            el.attr('href', result.UpdatedHref);
                        }
                    });
                    return cancelEvent(e);
                });

                $this.on('click', '.delete', function (e) {
                    var target = $(e.target);
                    var tr = target.parents('.fields_container');
                    var hiddenIsDeleted = $('.hiddenIsDeleted', tr);
                    if (hiddenIsDeleted.length > 0) {
                        hiddenIsDeleted.val('true');
                        tr.hide();
                    } else {
                        tr.remove();
                    }

                    return cancelEvent(e);
                });

                $this.on('click', '.chkcurrent', function (e) {
                    var parent = $(e.target).parents('.fields_container');
                    parent.toggleClass('current');
                    parent.toggleClass('historic');
                });

                $this.on('click', '[data-role="SendConfirmation"]', function (e) {
                    loadAsync(e, $(e.target).attr('href'), null, function (result) {
                        if (result) {
                            $(e.target).parent().find('[data-role="SendingSuccess"]').show();
                        }
                    });
                    return cancelEvent(e);
                });
            });
        }
    };
})(jQuery);

//jquery plugin template
(function ($) {
    $.fn.photo = function(method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.photo.defaults = {
        
    };

    var methods = {
        init: function(options) {
            var opts = $.extend({ }, $.fn.photo.defaults, options);

            function editPhoto(e) {
                $('#imgProfilePhoto').hide();
                $('#formFileUploadContainer').show();
                $('#lnkCancelPhotoEdit').show();
            }

            function ensurePhotoVisible(e) {
                $('#imgProfilePhoto').show();
                $('#formFileUploadContainer').hide();
                $('#uploaderContainer').hide();
            }

            function doUpload(e) {
                $('#uploaderContainer').show();
                $('#formFileUpload').submit();
            }

            return this.each(function() {
                $('#lnkChangePhoto').click(editPhoto);
                $('#hpf').change(doUpload);
                $('#imgProfilePhoto').load(ensurePhotoVisible);
                $('#lnkCancelPhotoEdit').click(ensurePhotoVisible);
            });
        }
    };
})(jQuery);

//jquery plugin template
(function($) {
    $.fn.like = function(method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.like.defaults = {
        callback: function () {
        }
    };

    var methods = {
        init: function(options) {
            var opts = $.extend({ }, $.fn.like.defaults, options);

            this.on('click', function(e) {
                var url = $(this).attr('href');
                loadAsync(e, url,
                    null,
                    function(result) {
                        var parent = $(e.target).parents('.likeParent');
                        $('span', parent).toggleClass('hide');
                        opts.callback();
                    });
                return cancelEvent(e);
            });
        }
    };
})(jQuery);