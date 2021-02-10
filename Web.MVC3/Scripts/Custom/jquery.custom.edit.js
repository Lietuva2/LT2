//jquery plugin template
(function ($) {
    $.fn.dropDownMultiList = function (options) {
        var opts = $.extend({}, $.fn.dropDownMultiList.defaults, options);

        return this.each(function () {
            var $this = $(this);

            $this.chosen(opts);
        });
    };

    $.fn.dropDownMultiList.defaults = {
        max_selected_options: 3,
        placeholder_text_multiple: JavaScriptLibraryResources.SelectCategories,
        no_results_text: JavaScriptLibraryResources.NoCategories,
        placeholder_text_single: JavaScriptLibraryResources.ChooseCategory
    };
})(jQuery);
