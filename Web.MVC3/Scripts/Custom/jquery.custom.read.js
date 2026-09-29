//jquery comment plugin
(function ($) {
    $.fn.CommentsRead = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }
    };

    $.fn.CommentsRead.defaults = {
        prepend: false,
        data: {},
        showMoreLinkSelector: '.lnkShowMoreComments',
        commentsListSelector: '.listComments',
        commentsListContainerSelector: null,
        showMoreOnScroll: true
    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.CommentsRead.defaults, options);

            return this.each(function () {
                var $this = $(this);

                $this.on('click', 'a.expandComments', function (e) {
                    var list = $(this).parents('.commentCommentsContainer').find('.hiddenInnerList');
                    list.slideToggle();
                    $('div.text_expandable', list).expandable();
                    return $.helpers.cancelEvent(e);
                });

                $.helpers.bindPreview($this);

                var commentsList = $this;
                if (opts.commentsListSelector) {
                    commentsList = $this.find(opts.commentsListSelector);
                }
                commentsList.showmore({ linkSelector: opts.showMoreLinkSelector, listSelector: opts.commentsListSelector, containerSelector: opts.commentsListContainerSelector, prepend: opts.prepend, data: opts.data });
                if (opts.showMoreOnScroll) {
                    initializeInfiniteScroll(opts.showMoreLinkSelector);
                }
            });
        }
    };

})(jQuery);
