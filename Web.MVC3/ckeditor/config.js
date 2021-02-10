/**
 * @license Copyright (c) 2003-2013, CKSource - Frederico Knabben. All rights reserved.
 * For licensing, see LICENSE.html or http://ckeditor.com/license
 */

CKEDITOR.editorConfig = function( config ) {
	// Define changes to default configuration here. For example:
    config.language = JavaScriptLibraryResources.LanguageCode;
    config.extraPlugins = 'onchange,oembed';
    //config.removePlugins = 'find,autogrow, forms, scayt,templates,a11yhelp,about,ajax,adobeair,bbcode,colordialog,devtools,div,iframe,docprops,flash,liststyle,pagebreak,placeholder,showblocks,smiley,specialchar,stylesheetparser,templates,uicolor,wsc,xml';
    config.toolbarGroups = [
        { name: 'document', groups: ['mode'] },
		{ name: 'basicstyles', groups: ['basicstyles', 'cleanup'] },
		{ name: 'paragraph', groups: ['list', 'indent', 'blocks', 'align'] },
        { name: 'links' },
		{ name: 'insert' },
        { name: 'others' }
    ];

    config.removeButtons = 'Subscript,Superscript,Anchor,Flash,Smiley,SpecialChar,CreateDiv,Blockquote,PageBreak,Save,NewPage,Preview,Print,Strike';
    // config.uiColor = '#AADC6E';
    // Let's have it basic on dialogs as well.
    config.removeDialogTabs = 'link:advanced';
};
