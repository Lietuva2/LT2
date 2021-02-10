function setupDatePicker() {
    $.datepicker.setDefaults($.datepicker.regional['lt']);
    $('.date').datepicker({
        changeMonth: true,
        changeYear: true
    });
    if (typeof ($.inputmask) == 'object') {
        $('.time').inputmask({ "mask": "99:99" });
    }
}

Date.prototype.addMonths = function(months) {
    var date = new Date();
    try {
        date.setMonth(date.getMonth() + months);
    } catch(ex) {
        date = new Date(date.getTime() + months * 30 * 24 * 60 * 60 * 1000);
    }

    return date;
};

function goBack() {
    window.oldhref = location.href;
    window.oldhash = location.hash;
    history.back();
    setTimeout(function () {
        if (location.href == window.oldhref && location.hash == window.oldhash) {
            window.close();
        }
    }, 500);

}

/***********************Dialogs*********************/

function CreateSendMessageDialog(linkSelector, dialogSelector, url, toName, toUserId) {
    linkSelector.linkdialog({ 
        height: 254, width: 500,
        dialogSelector: dialogSelector,
        openCallback: function () {
            $('#divMessage', dialogSelector).hide();
            $('#divMessageForm', dialogSelector).show();
            $('#lblMessageTo', dialogSelector).text(toName);
            $('#MessageToUserObjectId', dialogSelector).val(toUserId);
        },
        buttons: [{ text: JavaScriptLibraryResources.Send,
                    click: function (e) {
                        loadAsync(e, url, $('form', dialogSelector).serialize(), function (result) {
                            if (result) {
                                $('#divMessage', dialogSelector).show();
                                $('#divMessageForm', dialogSelector).hide();
                                setTimeout(function () { dialogSelector.dialog("close"); }, 2000);
                            }
                        });
                    },
                    className: "save"
                }]
    });
}

function updateEditorElements() {
    if (typeof (CKEDITOR) != "undefined" && CKEDITOR.instances) {
        for (var instance in CKEDITOR.instances) {
            CKEDITOR.instances[instance].updateElement();
        }
    }
}

function CreateSavingDialog(linkSelector, dialogSelector, callback, openCallback) {
    var form = $('form', dialogSelector);

    linkSelector.linkdialog({
        modal: false,
        dialogSelector: dialogSelector,
        openCallback: openCallback,
        buttons: [{ text: JavaScriptLibraryResources.Save,
            click: function (e) {

                updateEditorElements();
                loadAsync(e, form.attr('action'), form.serialize(), function (result) {
                    if (result) {
                        if (typeof (callback) == 'function') {
                            callback();
                        }
                        setTimeout(function () { dialogSelector.dialog("close"); }, 2000);
                    }
                });
            },
            className: "save"
        }]
    });
}

function CreateAjaxDialog(linkSelector, title, width, height) {
    width = $.helpers.checkDefault(width, 500);
    height = $.helpers.checkDefault(height, 400);
    $(linkSelector).linkdialog({ height: height, width: width, headerText: title, isAjax: true, openCallback: ajaxDialogOpenCallback });
}

function CreateUserAjaxDialog(linkSelector) {
    CreateAjaxDialog(linkSelector, JavaScriptLibraryResources.Users);
}

function ajaxDialogOpenCallback(result) {
    $('#divDialogContent').html(result.Content);
    $(document).showmore({linkSelector:'#divDialogContent #lnkShowMoreDialog', listSelector: '#divDialogContent #list' });
    $('[data-role="isPublicCheck"]').on('change', function (e) {
        $.helpers.loadAsync(e, $(this).attr('data-url'), { isPublic: $(this).is(':checked') }, function(res) {
            
        });
    });
}

/***********************************************end show more functionality**************************/

function loadAsync(e, url, data, callback, showLoader, hideLink, errorCallback) {
    return $.helpers.loadAsync(e, url, data, callback, showLoader, hideLink, errorCallback);
}

function initAutocomplete(selector, url, parentControlSelectors, paramName, mustMatch, idFieldSelector, selectCallBack) {
    selector.customautocomplete(url, { parentControlSelectors: parentControlSelectors, paramName: paramName, mustMatch: mustMatch, idFieldSelector: idFieldSelector, selectCallBack: selectCallBack });
}

function cancelEvent(e) {
    return $.helpers.cancelEvent(e);
}

/*****************************Category selection********************/
function CreateCategorySelector(filterPageUrl, saveCategoriesUrl) {
    $('#selectedCategoryIds').multiselector({
        filterPageUrl: filterPageUrl, 
        saveUrl: saveCategoriesUrl,
        invertButtonId: '#lnkInvertCategories',
        saveButtonId: '#lnkSaveCategories',
        revertButtonId: '#lnkRevert',
        categoryTagSelector: ".categories .ctag"
    });
}

function CreateStateSelector(filterPageUrl) {
    $('#selectedStateIds').multiselector({
        filterPageUrl: filterPageUrl,
        allItemsText: JavaScriptLibraryResources.AllStates,
        itemCountText: JavaScriptLibraryResources.StateCount
    });
}

/************************End category selection*************************/
function initializeInfiniteScroll(linkSelector) {
    linkSelector = $.helpers.checkDefault(linkSelector, '#lnkShowMore');
    var el = document;
    if ($.browser.msie && parseInt($.browser.version, 10) < 9) {
        el = window;
    }
    $(el).endlessScroll({
        fireOnce: true,
        fireDelay: 500,
        callback: function () {
            var link = $(linkSelector);
            if (!link.is(':hidden')) {
                link.click();
            }
        }
    });
}

function initTooltips() {

    $('[showtip],[mouseovertip]').each(function () {
        var parent = $(this);
        var target = parent.add(parent.find('input, select, textarea'));

        $(this).qtip({ position: {
            my: 'left top', // Use the corner...
            at: 'right center',
            target: parent // ...and opposite corner
        },
            show: {
                target: target,
                event: 'mouseenter', //parent.is('[showtip]') ? 'focus click' : 'mouseenter',
                solo: true // Only show one tooltip at a time
            },
            hide:{ //moved hide to here,
            delay:100, //give a small delay to allow the user to mouse over it.
            fixed:true
            },
            content: {
                title: {
                    text: JavaScriptLibraryResources.Help,
                    button: false
                }
            }
        });
    });
}

function closeTooltips() {
    $('[showtip],[mouseovertip]').qtip('hide');
}

function createExpandableText(container) {
    container = $.helpers.checkDefault(container, $(document));
    $('div.text_expandable', container).expandable();
}