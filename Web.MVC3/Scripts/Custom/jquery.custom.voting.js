(function ($) {
    var postedToFacebook = false;
    var showVersionTempCallback;
    var versionCompareDialogOpen = false;
    var isNewVersion = false;

    $.fn.voting = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    var methods = {
        init: function (options) {
            var opts = $.extend({}, $.fn.voting.defaults, options);
            var $this = this, versionsVisible, timeout;;

            if (opts.dialogSelector) {
                $(opts.dialogSelector).dialog({
                    resizable: false,
                    modal: false,
                    autoOpen: false,
                    width: 600,
                    height: 'auto',
                    closeOnEscape: false,
                    open: function (event, ui) { $(this).parent().children().children('.ui-dialog-titlebar-close').hide(); }
                });
            }
            createInputDialog();

            $this.on('click', opts.editSelector, edit);
            $this.on('click', opts.versionSelector, showVersion);
            $this.on('click', opts.deleteVersionSelector, deleteVersion);
            $this.on('click', opts.voteButtonSelector, vote);
            $this.on('click', opts.cancelVoteSelector, cancelVote);
            $this.on('click', '[data-role="subscribe"]', function (e) {
                var $el = $(this);
                $.helpers.loadAsync(e, $el.attr('href'), null, function (result) {
                    $el.parent().html(result.Content);
                });

                return $.helpers.cancelEvent(e);
            });

            if (opts.getVersionsUrl) {
                $this.on('click', 'a[data-role="expand"]', function (e) {
                    var childs = $(this).parents('.issue').find('div[data-role="versions"]');
                    if (childs.children().length > 0) {
                        if (!childs.is(':visible')) {
                            childs.slideDown();
                        } else {
                            childs.slideUp();
                        }
                    } else {
                        var id = childs.data('id');
                        $.helpers.loadAsync(e, opts.getVersionsUrl, { id: id }, function (result) {
                            $('div[data-role="versions"]').not(childs).slideUp(function () {
                                $(this).html('');
                            });
                            childs.hide();
                            //$(opts.dialogSelector).dialog('destroy').remove();
                            childs.html(result.Content);
                            if (result.VersionInput) {
                                $(opts.dialogSelector).html(result.VersionInput);
                            }

                            childs.slideDown(function () {
                                createInputDialog();
                            });
                        }
                        );
                    }

                    return $.helpers.cancelEvent(e);
                });
            }

            if (opts.hover) {
                bindHover();
            }

            $('#btnCompareVersions').linkdialog({
                width: 700,
                height: 400,
                headerText: 'Palyginimas',
                isAjax: true,
                openCallback: function (result) {
                    if (result) {
                        $('#divDialogContent').html(result.Content);
                        versionCompareDialogOpen = true;
                    } else {
                        alert('Nepavyko palyginti versijų');
                        return false;
                    }
                },
                closeCallback: function () {
                    setTimeout(function () { versionCompareDialogOpen = false; }, 100);
                },
                data: function () {
                    var id1 = "", id2 = "";
                    var radio = $("input[type='radio'][name='historyId1']:checked");
                    if (radio.length > 0)
                        id1 = radio.val();

                    radio = $("input[type='radio'][name='historyId2']:checked");
                    if (radio.length > 0)
                        id2 = radio.val();

                    return { historyId1: id1, historyId2: id2 };
                }
            });

            bindShowVersionButton();

            if (opts.hover) {
                $(document).click(function (e) {
                    if (versionsVisible && !versionCompareDialogOpen && $(e.target).parents(opts.hoverTargetSelector).length == 0 && $(e.target).parents(opts.summaryVersionsSelector).length == 0) {
                        $(opts.summaryVersionsSelector).hide();
                        versionsVisible = false;

                        bindHover();
                    }
                });
            }

            if (window.location.hash) {
                $this.find(opts.versionSelector).filter('[data-id=' + window.location.hash.substring(1) + ']:first').click();
            }

            function createInputDialog(container) {
                if (!container) {
                    container = $(document);
                }

                if (opts.allowNewVersion) {
                    container.find(opts.newVersionSelector).on('click', function (e) {
                        edit(e, true);
                    });
                }
                container.find('[data-role="googleDocs"]').googleDocs({ titleSelector: '#txtVersionSubject', formSelector: '#formVersion', showPrompt: true });

                container.find('.richtexteditor').richtext();
                if (typeof ($.fn.ajaxForm) == 'function') {
                    container.find(opts.formSelector).ajaxForm(submitSuccess);
                }

                container.find(opts.formCancelSelector).on('click', function (e) { closeForm(isNewVersion); });
            }

            function submitSuccess(result, status, xhr, form) {
                if ($.helpers.handleUnauthorizedError(result)) {
                    return false;
                }
                var errors = {};
                if (result.error) {
                    errors[form.find('textarea').attr('name')] = result.error;
                } else {
                    $this.find(opts.versionTextSelector).html(result.Text);
                    if (result.VersionId) {
                        $(opts.versionIdSelector).val(result.VersionId);
                    }

                    closeForm(true);

                    $this.find(opts.versionsSelector).html(result.Versions);

                    if (result.VotingStatistics) {
                        $this.find(opts.votingStatisticsSelector).html(result.VotingStatistics);
                    }
                    if (result.VotingButtons) {
                        $this.find(opts.votingButtonsSelector).html(result.VotingButtons);
                    }

                    bindShowVersionButton();
                    if (opts.allowNewVersion) {
                        $this.find(opts.newVersionSelector).on('click', function (e) {
                            edit(e, true);
                        });
                    }

                    errors[form.find('textarea').attr('name')] = null;
                }

                form.validate().showErrors(errors);
            }

            function closeForm(success) {
                if (opts.dialogSelector) {
                    $(opts.dialogSelector).dialog('close');
                }
                else {
                    $this.find(opts.formContainerSelector).hide();
                    $this.find(opts.readContainerSelector).show();
                }

                if (opts.hover) {
                    bindHover();
                }

                if (!success) {
                    return false;
                }

                $this.find(opts.versionContainerSelector).filter('tr.selected').find(opts.versionSelector + ':first').click();

                if (typeof (closeTooltips) == 'function') {
                    closeTooltips();
                }
            }

            function edit(e, createNewVersion) {
                if (!createNewVersion && $(this).data('url')) {
                    showVersion(e, $(this));
                }
                if (opts.dialogSelector) {
                    $($.helpers.settings.dialogSelector).parent().find('.ui-dialog-title').text($(e.target).text());
                    $(opts.dialogSelector).dialog('open');
                }
                else {
                    $this.find(opts.formContainerSelector).show();
                    $this.find(opts.readContainerSelector).hide();
                }

                isNewVersion = false;

                if (opts.allowNewVersion) {
                    if (createNewVersion === true) {
                        isNewVersion = true;
                        $('#CurrentVersion_CreateNewVersion').val(true);
                        $('#organizationContainer').toggle(!opts.isPrivateToOrganization);
                        if (opts.organizationId) {
                            $('#OrganizationId[value=' + opts.organizationId + ']').attr('checked', 'checked');
                        }
                        else {
                            $('#OrganizationId:first').attr('checked', 'checked');
                        }
                        $('#txtVersionSummary').val('');
                        $.helpers.setRichEditorText("txtVersionSummary", '');
                        $('#txtVersionSubject').val('');
                        $('#listAttachments').html('');
                        //$this.find(opts.editSelector).hide();
                    } else {
                        $('#CurrentVersion_CreateNewVersion').val(false);
                        $('#organizationContainer').hide();
                    }
                }

                if (opts.hover) {
                    versionsVisible = false;
                    unhover(true);
                    unbindHover();
                }

                $(document).googleDocs('reset');

                return cancelEvent(e);
            }

            function showVersion(e, target) {
                if (opts.showVersionAsync === false) {
                    return true;
                }
                if (!target) {
                    target = $(e.target);
                }

                if (!target.is('a')) {
                    return;
                }

                target.parents(opts.versionsSelector).find('.selected').removeClass('selected');
                target.parents(opts.versionContainerSelector).addClass('selected');
                target.parents(opts.versionContainerSelector).removeClass('bold');
                //$(window).scrollTop(0);

                loadAsync(e, target.data('url'), null, function (result) {
                    if (result.IsForHistory) {
                        var textRow = target.parents(opts.versionContainerSelector).next();
                        var versionText = textRow.find(opts.versionTextSelector);
                        versionText.html(result.Text);
                        return;
                    } else {
                        var textRow = target.parents(opts.versionContainerSelector).next();
                        var versionTextContainer = textRow.find(opts.versionTextContainerSelector);
                        versionTextContainer.find('.richtext').html(result.Text);

                        if (result.VersionId) {
                            $(opts.versionIdSelector).val(result.VersionId);
                        }

                        if (result.VotingStatistics) {
                            $this.find(opts.votingStatisticsSelector).html(result.VotingStatistics);
                        }
                        if (result.VotingButtons) {
                            $this.find(opts.votingButtonsSelector).html(result.VotingButtons);
                        }

                        if (result.UserFullName && result.UserProfileLink) {
                            $this.find(opts.createdBySelector).text(result.UserFullName).attr('href', result.UserProfileLink);
                        }

                        if (result.CreatedOn) {
                            $('#lblCreatedOn').text(result.CreatedOn);
                        }

                        if (result.VersionId) {
                            window.location.hash = result.VersionId;
                        }

                        textRow.find('[data-role="listDocsContainer"]').html(result.Documents);
                        $('#listAttachments').html($(result.Documents).find('ul[data-role="listDocs"]'));

                        $('#txtVersionSummary').val(result.Text);
                        $('#txtVersionSubject').val(result.Subject);
                        $.helpers.setRichEditorText("txtVersionSummary", result.Text);
                        //$this.find(opts.editSelector).toggle(result.Editable);

                        opts.showVersionCallback();
                    }
                });

                cancelEvent(e);
            }

            function deleteVersion(e) {
                if (confirm('Ar tikrai norite pašalinti šią versiją?')) {
                    var url = $(this).attr('href');
                    loadAsync(e, url,
                        null,
                        function (result) {
                            if (result) {
                                var parent = $(e.target).parents(opts.versionsSelector);
                                $(e.target).parents(opts.versionContainerSelector).remove();
                                parent.find(opts.versionContainerSelector + ':first a.version').click();
                            }
                        });
                }

                cancelEvent(e);
            }

            function vote(e) {
                var link = $(this);
                var url = typeof link.data('href') != "undefined" ? link.data('href') : link.attr('href');
                if (opts.isAmbasador && !postedToFacebook) {
                    if (!$(this).attr('data-private') || $(this).attr('data-private') == 'false') {
                        $.helpers.postToFacebook(opts.supportMessage, opts.facebookUrl.replace('{0}', $(this).attr('data-id')), opts.forceFbLogin);
                        postedToFacebook = true;
                    }
                }
                loadAsync(e, url, null, function (result) {
                    if (result.Error) {
                        $.helpers.handleUniqueUserError(result);

                        return false;
                    }

                    if (opts.isMainIdeaVote) {
                        $('divMainIdeaVoting').hide();
                    }

                    $('.voting').hide();

                    if (result.Progress) {
                        var parent = link.parents('div[data-role="unique-voting"]:first');
                        if (parent.length > 0) {
                            parent.find('div[data-role="progressbar"]').html(result.Progress).show();
                            parent.find('div[data-role="votebutton"]').hide();
                            if (result.ThankYou) {
                                parent.find('div[data-role="thank-you"]').html(result.ThankYou);
                            }
                            return true;
                        }
                    }

                    if (result.VotingStatistics) {
                        $this.find(opts.votingStatisticsSelector).html(result.VotingStatistics);
                    }
                    if (result.VotingButtons) {
                        $this.find(opts.votingButtonsSelector).html(result.VotingButtons);
                    }
                    $this.find(opts.versionsSelector).html(result.Versions);

                    if (result.Subscribe) {
                        $('.statistics_buttons [data-role="subscribe"]').parent().html(result.Subscribe);
                        $this.parents('.problem_container').find('[data-role="subscribe"]').parent().html(result.Subscribe);
                    }
                });
                return cancelEvent(e);
            }

            function cancelVote(e) {
                loadAsync(e, $(this).attr('href'), null, function (result) {
                    if (result.VotingStatistics) {
                        $this.find(opts.votingStatisticsSelector).html(result.VotingStatistics);
                    }
                    if (result.VotingButtons) {
                        $this.find(opts.votingButtonsSelector).html(result.VotingButtons);
                    }
                    $this.find(opts.versionsSelector).html(result.Versions);
                });

                return cancelEvent(e);
            }

            function bindHover() {
                $(opts.hoverTargetSelector).hover(hover, unhover);
            }

            function unbindHover() {
                $(opts.hoverTargetSelector).unbind('mouseenter').unbind('mouseleave');
            }


            function hover() {
                clearTimeout(timeout);
                $(this).find('.summaryHover').show();
                if (opts.highlightOnHover) {
                    $(opts.hoverTargetSelector).addClass('desc_highlight');
                }
            }

            function unhover(nodelay) {
                if (nodelay === true) {
                    $('.summaryHover').hide();
                    if (opts.highlightOnHover) {
                        $(opts.hoverTargetSelector).removeClass('desc_highlight');
                    }
                }
                else {
                    timeout = setTimeout(function () {
                        $('.summaryHover').hide();
                        if (opts.highlightOnHover) {
                            $(opts.hoverTargetSelector).removeClass('desc_highlight');
                        }
                    }, 1000);
                }
            }

            function bindShowVersionButton() {
                $(opts.showVersionsButtonSelector).click(function (e) {
                    if (opts.versionTextContainerSelector) {
                        $(this).parents(opts.versionTextContainerSelector).find(opts.summaryVersionsSelector).show();
                    } else {
                        $(opts.summaryVersionsSelector).show();
                    }

                    if (opts.hover) {
                        unhover(true);
                        versionsVisible = true;
                        unbindHover();
                    }
                });
            }
        }


    };

    $.fn.voting.defaults = {
        formSelector: 'form#formVersion',
        formCancelSelector: '#btnCancel',
        editSelector: '#lnkEditVersion',
        newVersionSelector: '#lnkNewVersion',
        allowNewVersion: true,
        versionSelector: 'a.version',
        deleteVersionSelector: 'a.deleteVersion',
        formContainerSelector: '#editSummary',
        readContainerSelector: '#summary',
        versionTextSelector: '#spanVersionText',
        versionTextContainerSelector: '',
        versionIdSelector: '#hiddenCurrentVersionId',
        versionsSelector: '#divSummaryVersions',
        votingStatisticsSelector: '#divVotingStatistics',
        votingButtonsSelector: '[data-role="votingButtons"]',
        versionContainerSelector: '.versionContainer',
        createdBySelector: '#lnkCreatedBy',
        voteButtonSelector: '.VoteButton',
        cancelVoteSelector: '#linkChangedMyMind',
        votingContainer: '#divVoting',
        votedContainer: '#divVoted',
        hover: false,
        hoverTargetSelector: "#divSummaryText",
        showVersionCallback: function () { },
        isAmbasador: false,
        supportMessage: "",
        facebookUrl: null,
        forceFbLogin: false,
        getVersionsUrl: null,
        dialogSelector: null,
        organizationId: null,
        isPrivateToOrganization: false,
        isMainIdeaVote: false,
        showVersionsButtonSelector: '#lnkShowVersions',
        summaryVersionsSelector: '#divSummaryVersions',
        highlightOnHover: true
    };

})(jQuery);

(function ($) {
    $.fn.problems = function (method) {

        if (methods[method]) {
            return methods[method].apply(this, Array.prototype.slice.call(arguments, 1));
        } else if (typeof method === 'object' || !method) {
            return methods.init.apply(this, arguments);
        } else {
            $.error('Method ' + method + ' does not exist on the plugin');
        }

    };

    $.fn.problems.defaults = {
        addIdeaUrl: null
    };

    var problemId, problemContainer, autocompleteHtml, opts = $.fn.problems.defaults;

    var methods = {
        init: function (options) {
            opts = $.extend({}, $.fn.problems.defaults, options);
            var $this = this;

            if ($.fn.dropDownMultiList) {
                $('#divProblemInput select').dropDownMultiList();
            }

            this.on('click', '.vote', function (e) {
                var $this = $(this);
                $.helpers.loadAsync(null, $this.attr('href'), null, function (result) {
                    var vote = $this.parents('[data-role="vote"]');
                    vote.html(result.Content);
                    if (result.Subscribe) {
                        vote.parents('.problem_container').find('[data-role="subscribe"]').parent().html(result.Subscribe);
                    }
                });

                return $.helpers.cancelEvent(e);
            });

            this.on('click', '.lnkAddComment', function (e) {
                var input = $(this).parents('.problem_item_details').find('.comment_input');
                input.toggle();
                $(this).toggle();
                input.find('textarea').focus();
                $.helpers.cancelEvent(e);
            });

            $('#txtShowProblemInput').on('focus', function (e) {
                $('#problemInput').show();
                $('#problemInput').find('textarea').focus();
                $(e.target).hide();
            });

            this.on('click', "a[data-role='add_idea']", function (e) {
                problemId = $(e.target).data('problem-id');
                problemContainer = $(e.target).parents('div.problem_container');
                problemContainer.find('.add_idea').toggle();
            });

            if (typeof ($.fn.ajaxForm) == 'function') {
                $('#formNewProblem').ajaxForm(function (result) {
                    if ($.helpers.handleUnauthorizedError(result)) {
                        return false;
                    }
                    if ($this.is('ul')) {
                        result.Content = "<li data-objectId=" + result.Id + ">" + result.Content + "</li>";
                    }
                    var html = $(result.Content).prependTo($this);
                    var text = $('#Text').val();
                    $('#Text').val('');
                    $('#problemInput').hide();
                    $('#txtShowProblemInput').show();
                    if (typeof ($.preview) == 'object') {
                        for (i = 0; i < $.preview.instances.length; i++) {
                            $.preview.instances[i].clearSelector();
                        }
                    }

                    if ($('#chkPostToFacebook').is(':checked')) {
                        var a = html.find('a.title');

                        $.helpers.postToFacebook(text, a.attr('href'), false, true, false);
                    }

                    if (typeof $.fn.autosize == 'function') {
                        html.find('textarea').autosize();
                    }

                    $.helpers.flash(html);
                });
                if (typeof ($.preview) == 'object') {
                    $('#formNewProblem').find('#Text').preview({ key: $.helpers.settings.embedlyKey });
                }
            }

            $.helpers.bindPreview($this);

            this.on('click', '.button_choose', function (e) {
                $(this).parents('.add_idea').find('div[data-role="add_idea"]').toggle();
                if (!autocompleteHtml) {
                    autocompleteHtml = $("div[data-role='add_idea_hidden']").html();
                    $("div[data-role='add_idea_hidden']").remove();
                }
                $("div[data-role='add_idea']").html('');
                $(e.target).parents('div.problem_container').find("div[data-role='add_idea']").html(autocompleteHtml);
                return $.helpers.cancelEvent(e);
            });

            function commentsInput(el) {
                if ($.fn.CommentsInput) {
                    el.CommentsInput('clear');
                    el.CommentsInput({
                        listSelector: ".listComments",
                        append: true,
                        callback: function (comment, form) {
                            var input = form.parents('.comment_input');
                            input.hide();
                            input.parent().find('.lnkAddComment').show();
                        }
                    });
                }
            }

            commentsInput($('.comments'));
            $(document).ajaxSuccess(function () {
                commentsInput($('.comments'));
            });

            this.on('click', "a[data-role='expand_problem']", function (e) {
                var parent = $(this).parents('.problem_container');

                var col = parent.find('div[data-role="expander"]');
                if (col.is(':visible')) {
                    $(e.target).text("Išskleisti");
                    col.slideUp();
                    parent.find('div[data-role="collapsed"]').show();
                }
                else {
                    $(e.target).text("Suskleisti");
                    col.slideDown(function () {
                        parent.find('div[data-role="collapsed"]').hide();
                    });

                }
            });

            //$('.lnkShowMoreComments').showmore({ listSelector: ".listComments", listContainer: "parent", append: false, prepend: true });
        },
        select: function () {
            $.helpers.loadAsync(null, opts.addIdeaUrl, { ideaId: $('#IdeaId').val(), problemId: problemId }, function (result) {
                var html = $(result.Content).appendTo(problemContainer.find("ul[data-role='relatedIdeas']"));
                problemContainer.find('.add_idea').hide();
                $.helpers.flash(html);
                $('#txtIdea').val('');
                $('#divAddIdea').toggle();
            });
        }
    };
})(jQuery);